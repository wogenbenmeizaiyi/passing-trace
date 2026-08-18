using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PassingTrace.Identity.AuthorizationServer.DependencyInjection;

/// <summary>允许浏览器 Token 请求的来源配置，只放行显式声明的 Web Origin。</summary>
public static class CorsExtensions
{
    /// <summary>
    /// 注册默认 CORS 策略，仅放行 <c>OpenIddict:AllowedOrigins</c> 中显式声明的 Web Origin。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">用于读取允许的浏览器来源列表。</param>
    public static IServiceCollection AddIdentityCors(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                var allowedOrigins = configuration
                    .GetSection("OpenIddict:AllowedOrigins")
                    .Get<string[]>() ?? [];

                // 仅 SPA 的浏览器 Token 请求需要 CORS；允许来源必须显式配置，不能使用通配符。
                policy.WithOrigins(allowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod();
            });
        });

        return services;
    }
}
