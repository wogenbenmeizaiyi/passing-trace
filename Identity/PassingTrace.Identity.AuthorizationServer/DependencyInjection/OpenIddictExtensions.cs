using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenIddict.Abstractions;
using PassingTrace.Identity.AuthorizationServer.Setup;
using PassingTrace.Identity.Infrastructure;

namespace PassingTrace.Identity.AuthorizationServer.DependencyInjection;

/// <summary>OpenIddict 协议服务器的 Core / Server / Validation 配置。</summary>
public static class OpenIddictExtensions
{
    /// <summary>
    /// 注册 OpenIddict 协议服务器：EF Core 存储、授权码 + PKCE + Refresh Token、
    /// Scope、Token 生命周期、证书加载与本地验证。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">用于读取 <c>OpenIddict:Issuer</c> 和证书路径。</param>
    /// <param name="environment">决定使用临时密钥（Testing）、开发证书（Development）还是持久证书。</param>
    public static IServiceCollection AddOpenIddictServer(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddOpenIddict()
            .AddCore(options =>
            {
                // OpenIddict 的客户端、授权、Scope、Token 与 Identity 用户共享 PostgreSQL。
                options.UseEntityFrameworkCore()
                    .UseDbContext<IdentityDbContext>()
                    .ReplaceDefaultEntities<long>();
            })
            .AddServer(options =>
            {
                options.SetAuthorizationEndpointUris("/connect/authorize")
                    .SetTokenEndpointUris("/connect/token")
                    .SetEndSessionEndpointUris("/connect/logout");

                // 只开放适合交互式第一方客户端的授权码和刷新流程，所有授权码强制 PKCE。
                options.AllowAuthorizationCodeFlow()
                    .AllowRefreshTokenFlow()
                    .RequireProofKeyForCodeExchange();
                options.Configure(configuration =>
                    configuration.CodeChallengeMethods.Remove(
                        OpenIddictConstants.CodeChallengeMethods.Plain));

                options.RegisterScopes(
                    OpenIddictConstants.Scopes.OpenId,
                    OpenIddictConstants.Scopes.Profile,
                    OpenIddictConstants.Scopes.OfflineAccess,
                    IdentityOpenIddictConstants.ApiScope,
                    IdentityOpenIddictConstants.LoginApprovalScope);

                // 短 Access Token 限制退出后的残余有效期；Refresh Token 提供长期会话。
                options.SetAccessTokenLifetime(TimeSpan.FromMinutes(15));
                options.SetRefreshTokenLifetime(TimeSpan.FromDays(30));
                options.SetRefreshTokenReuseLeeway(TimeSpan.Zero);
                // 业务服务需独立验签，因此 Access Token 保持"签名但不加密"的标准 JWT。
                options.DisableAccessTokenEncryption();

                var issuer = configuration["OpenIddict:Issuer"];
                if (!string.IsNullOrWhiteSpace(issuer))
                {
                    options.SetIssuer(new Uri(issuer, UriKind.Absolute));
                }

                // 测试密钥不落盘；开发证书由 OpenIddict 管理；生产必须显式提供持久证书。
                if (environment.IsEnvironment("Testing"))
                {
                    options.AddEphemeralEncryptionKey()
                        .AddEphemeralSigningKey();
                }
                else if (environment.IsDevelopment())
                {
                    options.AddDevelopmentEncryptionCertificate()
                        .AddDevelopmentSigningCertificate();
                }
                else
                {
                    options.AddEncryptionCertificate(LoadCertificate(
                        configuration,
                        "OpenIddict:Certificates:Encryption"));
                    options.AddSigningCertificate(LoadCertificate(
                        configuration,
                        "OpenIddict:Certificates:Signing"));
                }

                var aspNetCore = options.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough()
                    .EnableStatusCodePagesIntegration();

                // Android 模拟器通过 10.0.2.2 访问开发机，开发环境显式允许 HTTP。
                // 非 Development 环境不会执行此配置，仍由 OpenIddict 强制 HTTPS。
                if (environment.IsDevelopment())
                {
                    aspNetCore.DisableTransportSecurityRequirement();
                }
            })
            .AddValidation(options =>
            {
                // Identity 自己的扫码批准 API 直接验证本服务签发的 JWT。
                options.UseLocalServer();
                options.UseAspNetCore();
            });

        return services;
    }

    private static X509Certificate2 LoadCertificate(
        IConfiguration configuration,
        string sectionPath)
    {
        var path = configuration[$"{sectionPath}:Path"];
        var password = configuration[$"{sectionPath}:Password"];

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException(
                $"非开发环境必须配置 {sectionPath}:Path。");
        }

        // EphemeralKeySet 避免容器实例把私钥写入宿主机用户密钥存储。
        return X509CertificateLoader.LoadPkcs12FromFile(
            path,
            password,
            X509KeyStorageFlags.EphemeralKeySet);
    }
}
