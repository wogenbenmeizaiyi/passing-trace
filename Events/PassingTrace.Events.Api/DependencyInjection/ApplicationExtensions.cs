using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using PassingTrace.Events.Api.Ai;
using PassingTrace.Events.Api.Ai.Amap;
using PassingTrace.Events.Api.Ai.Capabilities;
using PassingTrace.Events.Api.Common;
using PassingTrace.Events.Api.Events;
using PassingTrace.Events.Api.Media;
using PassingTrace.Events.Api.Places;
using PassingTrace.Events.Api.Updates;
using PassingTrace.Events.Api.Storylines;

namespace PassingTrace.Events.Api.DependencyInjection;

/// <summary>注册业务应用服务与 MVC 控制器。</summary>
public static class ApplicationExtensions
{
    public const string WebClientCorsPolicy = "PassingTraceWebClient";

    /// <summary>注册应用编排服务、时间提供器、控制器与异常处理。</summary>
    public static IServiceCollection AddTraceApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var allowedOrigins = configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>()?
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Select(origin => origin.TrimEnd('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

        services.AddCors(options => options.AddPolicy(WebClientCorsPolicy, policy =>
        {
            if (allowedOrigins.Length > 0)
            {
                policy.WithOrigins(allowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .WithExposedHeaders("ETag", "Version");
            }
        }));
        services.AddSingleton(TimeProvider.System);
        services.Configure<ObjectStorageOptions>(configuration.GetSection(ObjectStorageOptions.SectionName));
        services.Configure<AiModelOptions>(configuration.GetSection(AiModelOptions.SectionName));
        services.AddOptions<AmapOptions>()
            .Bind(configuration.GetSection(AmapOptions.SectionName))
            .PostConfigure(options =>
            {
                options.McpKey = FirstConfigured(configuration["AMAP_MCP_KEY"], options.McpKey);
                options.WebServiceKey = FirstConfigured(configuration["AMAP_WEB_SERVICE_KEY"], options.WebServiceKey);
            });
        services.AddHttpClient<AmapPlaceService>(client =>
        {
            client.BaseAddress = new Uri("https://restapi.amap.com");
            client.Timeout = TimeSpan.FromSeconds(8);
        }).RemoveAllLoggers();
        services.AddHttpClient<AmapMcpGateway>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(12);
        }).RemoveAllLoggers();
        services.AddScoped<IAmapMcpGateway>(provider => provider.GetRequiredService<AmapMcpGateway>());
        services.AddScoped<IAmapQuotaGuard, RedisAmapQuotaGuard>();
        services.AddSingleton<AiClientFactory>();
        services.AddSingleton(provider => provider.GetRequiredService<AiClientFactory>().AssistantChatClient);
        services.AddSingleton(provider => provider.GetRequiredService<AiClientFactory>().EmbeddingGenerator);
        services.AddHttpContextAccessor();
        services.AddScoped<CurrentUserContext>();
        services.AddScoped<PersonalRecordTools>();
        services.AddScoped<AmapAiTools>();
        services.AddScoped<IAiCapabilityPackage, PersonalRecordsCapabilityPackage>();
        services.AddScoped<IAiCapabilityPackage, AmapCapabilityPackage>();
        services.AddScoped<AssistantService>();
        services.AddScoped<UserMemoryService>();
        services.AddSingleton<IObjectStorage, S3ObjectStorage>();
        services.Configure<AppUpdateOptions>(configuration.GetSection(AppUpdateOptions.SectionName));
        services.AddSingleton<AppUpdateService>();
        services.AddScoped<IAnalysisOutbox, AnalysisOutbox>();
        services.AddScoped<MediaService>();
        services.AddScoped<IEventMediaService>(provider => provider.GetRequiredService<MediaService>());
        services.AddScoped<EventService>();
        services.AddScoped<StorylineService>();
        // 保留 action 名中的 Async 后缀，使 CreatedAtAction(nameof(...)) 生成的路由能匹配。
        services.AddControllers(options =>
            options.SuppressAsyncSuffixInActionNames = false);

        services.AddProblemDetails();
        services.AddExceptionHandler<DomainExceptionHandler>();

        return services;
    }

    private static string FirstConfigured(string? preferred, string fallback) =>
        string.IsNullOrWhiteSpace(preferred) ? fallback : preferred.Trim();
}
