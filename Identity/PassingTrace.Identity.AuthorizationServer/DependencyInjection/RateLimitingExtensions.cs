using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace PassingTrace.Identity.AuthorizationServer.DependencyInjection;

/// <summary>移动注册、登录启动与扫码批准接口的固定窗口限流。</summary>
public static class RateLimitingExtensions
{
    /// <summary>
    /// 为移动注册、登录启动和扫码批准接口注册固定窗口限流器，拒绝时返回 429。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="isTesting">测试环境放大限流阈值，避免集成测试被限流误伤。</param>
    public static IServiceCollection AddIdentityRateLimiting(
        this IServiceCollection services,
        bool isTesting)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddFixedWindowLimiter("mobile-registration", limiter =>
            {
                limiter.PermitLimit = isTesting ? 1_000 : 10;
                limiter.Window = TimeSpan.FromMinutes(10);
                limiter.QueueLimit = 0;
            });
            options.AddFixedWindowLimiter("mobile-launch", limiter =>
            {
                limiter.PermitLimit = isTesting ? 1_000 : 20;
                limiter.Window = TimeSpan.FromMinutes(10);
                limiter.QueueLimit = 0;
            });
            options.AddFixedWindowLimiter("qr-approval", limiter =>
            {
                limiter.PermitLimit = isTesting ? 1_000 : 30;
                limiter.Window = TimeSpan.FromMinutes(1);
                limiter.QueueLimit = 0;
            });
        });

        return services;
    }
}
