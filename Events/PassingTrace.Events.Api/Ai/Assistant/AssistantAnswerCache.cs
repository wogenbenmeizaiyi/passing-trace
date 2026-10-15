using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using PassingTrace.Events.Api.Ai.Assistant.Context;
using PassingTrace.Events.Api.Ai.Evidence;
using PassingTrace.Events.Api.Ai.Models;
using PassingTrace.Events.Api.Ai.Skills;
using StackExchange.Redis;

namespace PassingTrace.Events.Api.Ai.Assistant;

public static class AssistantAnswerCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };
    public static string BuildKey(long userId, string question, string conversationContext, long watermark, AiModelOptions options,
        AssistantCalendarContext calendar)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{userId}\n{question}\n{conversationContext}\n{watermark}\n{options.Assistant.Provider}\n{options.Assistant.PrimaryModel}\n{options.PromptVersion}\n{AssistantSkillCatalog.Version}\n{calendar.CacheValue}"));
        return $"passingtrace:ai:answer:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    public static CachedAnswer? Read(RedisValue cached)
    {
        if (!cached.HasValue) return null;
        try
        {
            var value = JsonSerializer.Deserialize<CachedAnswer>(cached.ToString(), JsonOptions);
            return !string.IsNullOrWhiteSpace(value?.Answer) && value.Evidence?.Records is not null &&
                value.Evidence.Memories is not null ? value : null;
        }
        catch (JsonException)
        {
            // 旧缓存或不完整缓存不是一次成功回答；重新执行正常检索流程。
            return null;
        }
    }

    public static string Serialize(string answer, EvidenceBundle evidence) =>
        JsonSerializer.Serialize(new CachedAnswer(answer, evidence), JsonOptions);

    public sealed record CachedAnswer(string Answer, EvidenceBundle Evidence);
}
