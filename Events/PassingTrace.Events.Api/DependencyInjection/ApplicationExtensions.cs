using Microsoft.Extensions.DependencyInjection;
using PassingTrace.Events.Api.Common;
using PassingTrace.Events.Api.Events;

namespace PassingTrace.Events.Api.DependencyInjection;

/// <summary>注册业务应用服务与 MVC 控制器。</summary>
public static class ApplicationExtensions
{
    /// <summary>注册应用编排服务、时间提供器、控制器与异常处理。</summary>
    public static IServiceCollection AddTraceApplication(
        this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<EventService>();
        // 保留 action 名中的 Async 后缀，使 CreatedAtAction(nameof(...)) 生成的路由能匹配。
        services.AddControllers(options =>
            options.SuppressAsyncSuffixInActionNames = false);

        services.AddProblemDetails();
        services.AddExceptionHandler<DomainExceptionHandler>();

        return services;
    }
}
