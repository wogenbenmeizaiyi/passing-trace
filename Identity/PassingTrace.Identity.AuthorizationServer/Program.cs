using System.Security.Cryptography.X509Certificates;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using Npgsql;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PassingTrace.Identity.AuthorizationServer.Setup;
using PassingTrace.Identity.AuthorizationServer.Mobile;
using PassingTrace.Identity.AuthorizationServer.QrLogin;
using PassingTrace.Identity.Infrastructure;
using PassingTrace.Identity.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Identity 负责用户、密码哈希、锁定与站点 Cookie；OpenIddict 在其上实现 OAuth 2.0/OIDC。
builder.Services.AddIdentityInfrastructure(
    builder.Configuration,
    builder.Environment.IsDevelopment());

builder.Services
    .AddOpenIddict()
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
        // 业务服务需独立验签，因此 Access Token 保持“签名但不加密”的标准 JWT。
        options.DisableAccessTokenEncryption();

        var issuer = builder.Configuration["OpenIddict:Issuer"];
        if (!string.IsNullOrWhiteSpace(issuer))
        {
            options.SetIssuer(new Uri(issuer, UriKind.Absolute));
        }

        // 测试密钥不落盘；开发证书由 OpenIddict 管理；生产必须显式提供持久证书。
        if (builder.Environment.IsEnvironment("Testing"))
        {
            options.AddEphemeralEncryptionKey()
                .AddEphemeralSigningKey();
        }
        else if (builder.Environment.IsDevelopment())
        {
            options.AddDevelopmentEncryptionCertificate()
                .AddDevelopmentSigningCertificate();
        }
        else
        {
            options.AddEncryptionCertificate(LoadCertificate(
                builder.Configuration,
                "OpenIddict:Certificates:Encryption"));
            options.AddSigningCertificate(LoadCertificate(
                builder.Configuration,
                "OpenIddict:Certificates:Signing"));
        }

        var aspNetCore = options.UseAspNetCore()
            .EnableAuthorizationEndpointPassthrough()
            .EnableTokenEndpointPassthrough()
            .EnableEndSessionEndpointPassthrough()
            .EnableStatusCodePagesIntegration();

        // Android 模拟器通过 10.0.2.2 访问开发机，开发环境显式允许 HTTP。
        // 非 Development 环境不会执行此配置，仍由 OpenIddict 强制 HTTPS。
        if (builder.Environment.IsDevelopment())
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

builder.Services.AddHostedService<OpenIddictSeeder>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<FirstPartyClientRegistry>();
builder.Services.Configure<MobileRegistrationOptions>(
    builder.Configuration.GetSection(MobileRegistrationOptions.SectionName));
builder.Services.Configure<QrLoginOptions>(
    builder.Configuration.GetSection(QrLoginOptions.SectionName));
builder.Services.AddScoped<MobileFlowService>();
builder.Services.AddScoped<QrLoginService>();
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("mobile-login-approval", policy =>
    {
        policy.AddAuthenticationSchemes(
            OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(context =>
            string.Equals(
                context.User.FindFirst(OpenIddictConstants.Claims.ClientId)?.Value,
                IdentityOpenIddictConstants.MobileClientId,
                StringComparison.Ordinal) &&
            context.User.HasScope(IdentityOpenIddictConstants.LoginApprovalScope));
    });
builder.Services.AddRateLimiter(options =>
{
    var registrationLimit = builder.Environment.IsEnvironment("Testing") ? 1_000 : 10;
    var launchLimit = builder.Environment.IsEnvironment("Testing") ? 1_000 : 20;
    var approvalLimit = builder.Environment.IsEnvironment("Testing") ? 1_000 : 30;

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("mobile-registration", limiter =>
    {
        limiter.PermitLimit = registrationLimit;
        limiter.Window = TimeSpan.FromMinutes(10);
        limiter.QueueLimit = 0;
    });
    options.AddFixedWindowLimiter("mobile-launch", limiter =>
    {
        limiter.PermitLimit = launchLimit;
        limiter.Window = TimeSpan.FromMinutes(10);
        limiter.QueueLimit = 0;
    });
    options.AddFixedWindowLimiter("qr-approval", limiter =>
    {
        limiter.PermitLimit = approvalLimit;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });
});
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var allowedOrigins = builder.Configuration
            .GetSection("OpenIddict:AllowedOrigins")
            .Get<string[]>() ?? [];

        // 仅 SPA 的浏览器 Token 请求需要 CORS；允许来源必须显式配置，不能使用通配符。
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var openTelemetry = builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(resource =>
        resource.AddService(builder.Environment.ApplicationName))
    .WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation();
        tracing.AddHttpClientInstrumentation();
        tracing.AddNpgsql();
    });

if (!string.IsNullOrWhiteSpace(
        builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
{
    openTelemetry.UseOtlpExporter();
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // 仅开发环境自动迁移；生产迁移应作为部署步骤单独执行。
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
    await dbContext.Database.MigrateAsync();
}
else
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseStatusCodePages();
app.UseRouting();
// OpenIddict 是 AuthenticationRequestHandler，CORS 必须位于认证中间件之前。
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapRazorPages();

app.Run();

static X509Certificate2 LoadCertificate(
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

/// <summary>暴露入口类型，供 WebApplicationFactory 启动内存测试宿主。</summary>
public partial class Program;
