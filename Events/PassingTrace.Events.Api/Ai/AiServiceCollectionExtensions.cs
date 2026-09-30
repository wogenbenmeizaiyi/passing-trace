using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PassingTrace.Core.Ai;
using PassingTrace.Events.Api.Ai.Amap;
using PassingTrace.Events.Api.Ai.Assistant;
using PassingTrace.Events.Api.Ai.Capabilities;
using PassingTrace.Events.Api.Ai.Memories;
using PassingTrace.Events.Api.Ai.Models;
using PassingTrace.Events.Api.Ai.Mutations;
using PassingTrace.Events.Api.Ai.Semantics;
using PassingTrace.Events.Api.Ai.Tools.Mutations;
using PassingTrace.Events.Api.Ai.Tools.Queries;
using PassingTrace.Events.Api.Places;
using PassingTrace.Events.Api.Social;
using PassingTrace.Infrastructure.Persistence.Ai;

namespace PassingTrace.Events.Api.Ai;

/// <summary>AI 模块的唯一注册入口：模型、工具、会话和持久化端口适配。</summary>
public static class AiServiceCollectionExtensions
{
    public static IServiceCollection AddTraceAi(this IServiceCollection services, IConfiguration configuration)
    {
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

        services.AddScoped<ISocialAiQueries, SocialAiQueries>();
        services.AddScoped<SocialAiTools>();
        services.AddScoped<IAiCapabilityPackage, FriendsCapabilityPackage>();
        services.AddScoped<IPersonalRecordQueries, PersonalRecordQueries>();
        services.AddScoped<IAiConversationRepository, AiConversationRepository>();
        services.AddScoped<IUserMemoryRepository, UserMemoryRepository>();
        services.AddScoped<IEventSemanticRepository, EventSemanticRepository>();
        services.AddScoped<EventSemanticService>();
        services.AddScoped<PersonalRecordTools>();
        services.AddScoped<IAiMutationRepository, AiMutationRepository>();
        services.AddScoped<AiMutationService>();
        services.AddScoped<PersonalMutationTools>();
        services.AddScoped<IAiCapabilityPackage, PersonalMutationsCapabilityPackage>();
        services.AddScoped<AmapAiTools>();
        services.AddScoped<IAiCapabilityPackage, PersonalRecordsCapabilityPackage>();
        services.AddScoped<IAiCapabilityPackage, AmapCapabilityPackage>();
        services.AddScoped<AssistantService>();
        services.AddScoped<UserMemoryService>();
        return services;
    }

    private static string FirstConfigured(string? preferred, string fallback) =>
        string.IsNullOrWhiteSpace(preferred) ? fallback : preferred.Trim();
}
