using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PassingTrace.Events.Api.Security;

namespace PassingTrace.Events.Api.DependencyInjection;

/// <summary>配置 JwtBearer 离线验签与默认 Scope 授权策略。</summary>
public static class AuthenticationExtensions
{
    /// <summary>
    /// 通过 Identity 的发现文档离线验证 Access Token，不访问 Identity 数据库。
    /// </summary>
    public static IServiceCollection AddTraceAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = configuration["Identity:Authority"];
                options.Audience = IdentityConstants.ApiAudience;
                options.MapInboundClaims = false;
                options.RequireHttpsMetadata = true;
            });

        services.AddAuthorizationBuilder()
            .AddDefaultPolicy(IdentityConstants.ApiScope, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireScope(IdentityConstants.ApiScope);
            });

        return services;
    }
}
