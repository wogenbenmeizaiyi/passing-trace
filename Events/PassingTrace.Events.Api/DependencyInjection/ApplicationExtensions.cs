using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PassingTrace.Core.Ai;
using PassingTrace.Events.Api.Ai;
using PassingTrace.Events.Api.Common;
using PassingTrace.Events.Api.Development;
using PassingTrace.Events.Api.Events;
using PassingTrace.Events.Api.Media;
using PassingTrace.Events.Api.Social;
using PassingTrace.Events.Api.Storylines;
using PassingTrace.Events.Api.Updates;
using PassingTrace.Infrastructure.Persistence.Ai;

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
        services.Configure<DevelopmentDemoOptions>(configuration.GetSection(DevelopmentDemoOptions.SectionName));
        services.AddScoped<DevelopmentDemoSeeder>();
        services.AddScoped<SocialDemoSeeder>();
        services.Configure<ObjectStorageOptions>(configuration.GetSection(ObjectStorageOptions.SectionName));
        services.AddTraceAi(configuration);
        services.AddHttpContextAccessor();
        services.AddHttpClient<ISocialIdentityClient, SocialIdentityClient>(client =>
        {
            client.BaseAddress = new Uri(configuration["Identity:Authority"]!.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(10);
        }).RemoveAllLoggers();
        services.AddScoped<FriendService>();
        services.AddScoped<SharedContentService>();
        services.AddScoped<DirectChatService>();
        services.AddScoped<EventParticipationService>();
        services.AddScoped<IEventParticipationService>(p => p.GetRequiredService<EventParticipationService>());
        services.AddScoped<CurrentUserContext>();
        services.AddSingleton<IObjectStorage, S3ObjectStorage>();
        services.Configure<AppUpdateOptions>(configuration.GetSection(AppUpdateOptions.SectionName));
        services.AddSingleton<AppUpdateService>();
        services.AddScoped<IAnalysisOutbox, AnalysisOutbox>();
        services.AddScoped<MediaService>();
        services.AddScoped<IEventMediaService>(provider => provider.GetRequiredService<MediaService>());
        services.AddScoped<EventService>();
        services.AddScoped<StorylineService>();
        services.AddScoped<Core.Subjects.ISubjectRepository, Infrastructure.Persistence.Subjects.SubjectRepository>();
        services.AddScoped<Core.Subjects.ISubjectMediaQueries, Infrastructure.Persistence.Subjects.SubjectRepository>();
        services.AddScoped<Subjects.SubjectService>();
        services.AddScoped<Core.Subjects.IEventSubjectService>(p => p.GetRequiredService<Subjects.SubjectService>());
        // 保留 action 名中的 Async 后缀，使 CreatedAtAction(nameof(...)) 生成的路由能匹配。
        services.AddControllers(options =>
            options.SuppressAsyncSuffixInActionNames = false);

        services.AddProblemDetails();
        services.AddExceptionHandler<DomainExceptionHandler>();

        return services;
    }
}
