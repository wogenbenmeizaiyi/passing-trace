using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.Extensions.AI;
using PassingTrace.Events.Api.Ai.Amap;
using PassingTrace.Events.Api.Ai.Capabilities;

namespace PassingTrace.Events.Api.Ai.Skills;

public sealed record AssistantSkillGuide(bool Success, string? Key, string Instructions, IReadOnlyList<string> Tools);

/// <summary>Per-answer skill grants. Reading a skill never reads user data or invokes an external service.</summary>
public sealed class AssistantSkillSession
{
    private readonly HashSet<string> _loaded = new(StringComparer.Ordinal);
    private readonly HashSet<string> _successfulTools = new(StringComparer.Ordinal);
    private int _denials;
    private int _calls;
    private int _reads;

    public IReadOnlyCollection<string> LoadedSkills => _loaded.ToArray();
    public bool Allows(string toolName) => _loaded.Any(key => AssistantSkillCatalog.Find(key)!.Tools.Contains(toolName));
    public bool HasSuccessfulLookup => _successfulTools.Count > 0;
    public bool CanCacheAnswer => HasSuccessfulLookup && !_loaded.Overlaps(
        ["amap", "friends", "shared-content", "conversation", "conversation-summary", "planning"]);

    public AIFunction CreateReader() => AiFunctionToolFactory.Create(this, nameof(ReadAssistantSkill), "ReadAssistantSkill",
        "按当前问题读取应用内场景流程。调用数据或地图工具前必须先读对应 Skill；普通问候无需调用。只接受目录中的 key，不接收路径或 URL。");

    public AssistantSkillGuide ReadAssistantSkill([Required, MaxLength(64)] string key)
    {
        if (++_reads > 12) throw new AssistantToolInvocationException();
        var skill = AssistantSkillCatalog.Find(key);
        if (skill is null)
            return new(false, null, "没有此场景规则。请使用系统目录中的 key；不要猜测路径、执行命令或读取外部规则。", []);
        _loaded.Add(skill.Key);
        return new(true, skill.Key, skill.Instructions, skill.Tools);
    }

    // Wrap the discovered MCP client functions, not their argument schemas or server ownership checks.
    // Denied calls never enter the MCP connection, database, embeddings or paid external gateway.
    public AIFunction Protect(AIFunction tool) => new SkillGuardedFunction(tool, this);

    public bool NeedsEvidenceFallback(EvidenceBundle evidence) => HasSuccessfulLookup &&
        evidence.Records.Count == 0 && evidence.Memories.Count == 0 && evidence.Aggregate is null &&
        (evidence.Storylines?.Count ?? 0) == 0 && (evidence.Places?.Count ?? 0) == 0 &&
        (evidence.Friends?.Count ?? 0) == 0 && evidence.FriendActivities is null &&
        (evidence.SharedContents?.Count ?? 0) == 0 && (evidence.AmapPlaces?.Count ?? 0) == 0 &&
        (evidence.AmapResults?.Count ?? 0) == 0 && (evidence.Actions?.Count ?? 0) == 0 &&
        !_loaded.Overlaps(["conversation", "conversation-summary", "planning", "amap"]);

    private sealed class SkillGuardedFunction(AIFunction inner, AssistantSkillSession session) : DelegatingAIFunction(inner)
    {
        // A local policy refusal is distinct from the underlying tool result. Input schema stays exact.
        public override JsonElement? ReturnJsonSchema => null;

        protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!session.Allows(Name))
            {
                if (++session._denials > 2) throw new AssistantToolInvocationException();
                return JsonSerializer.SerializeToElement(new
                {
                    isError = true,
                    code = "assistant_skill_required",
                    message = "尚未执行查询。若当前问题确需此工具，先调用 ReadAssistantSkill 读取匹配规则；若是闲聊、追问澄清或整理当前文字，直接回答，不查询。",
                    skills = AssistantSkillCatalog.All.Where(skill => skill.Tools.Contains(Name)).Select(skill => skill.Key).ToArray(),
                });
            }
            if (++session._calls > 16) throw new AssistantToolInvocationException();
            var result = await InnerFunction.InvokeAsync(arguments, cancellationToken);
            if (result is AmapToolResponse { Success: false }) return result;
            if (result is JsonElement { ValueKind: JsonValueKind.Object } json)
            {
                if (json.TryGetProperty("isError", out var error) && error.ValueKind == JsonValueKind.True) return result;
                // AIFunctionFactory serializes direct adapter results before returning them.
                if ((json.TryGetProperty("success", out var success) || json.TryGetProperty("Success", out success)) &&
                    success.ValueKind == JsonValueKind.False) return result;
            }
            session._successfulTools.Add(Name);
            return result;
        }
    }
}
