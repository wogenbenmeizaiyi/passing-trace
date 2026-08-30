using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using PassingTrace.Events.Api.Ai;
using PassingTrace.Events.Api.Common;
using PassingTrace.Events.Api.Events;
using PassingTrace.Events.Api.Media;

namespace PassingTrace.Events.Api.DependencyInjection;

/// <summary>注册业务应用服务与 MVC 控制器。</summary>
public static class ApplicationExtensions
{
    /// <summary>注册应用编排服务、时间提供器、控制器与异常处理。</summary>
    public static IServiceCollection AddTraceApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);
        services.Configure<ObjectStorageOptions>(configuration.GetSection(ObjectStorageOptions.SectionName));
        services.Configure<QwenAiOptions>(configuration.GetSection(QwenAiOptions.SectionName));
        services.AddSingleton<QwenClientFactory>();
        services.AddSingleton(provider => provider.GetRequiredService<QwenClientFactory>().ChatClient);
        services.AddSingleton(provider => provider.GetRequiredService<QwenClientFactory>().EmbeddingGenerator);
        services.AddHttpContextAccessor();
        services.AddScoped<CurrentUserContext>();
        services.AddScoped<PersonalRecordTools>();
        services.AddScoped<AssistantService>();
        services.AddScoped<UserMemoryService>();
        services.AddSingleton<IObjectStorage, S3ObjectStorage>();
        services.AddScoped<IAnalysisOutbox, AnalysisOutbox>();
        services.AddScoped<MediaService>();
        services.AddScoped<IEventMediaService>(provider => provider.GetRequiredService<MediaService>());
        services.AddScoped<EventService>();
        // 保留 action 名中的 Async 后缀，使 CreatedAtAction(nameof(...)) 生成的路由能匹配。
        services.AddControllers(options =>
            options.SuppressAsyncSuffixInActionNames = false);

        services.AddProblemDetails();
        services.AddExceptionHandler<DomainExceptionHandler>();

        return services;
    }
}
