using System.Security.Cryptography;
using System.Text;

namespace PassingTrace.Events.Api.Ai.Skills;

public sealed record AssistantSkillDefinition(string Key, string Description, IReadOnlyList<string> Tools, string Instructions);

/// <summary>Trusted, versioned application resources, never files or URLs supplied by a user/tool.</summary>
public static class AssistantSkillCatalog
{
    private static readonly string[] RecordTools =
        ["SearchMyRecords", "GetMyRecordEvidence", "SearchMyMemories", "SearchMyPlaces", "GetMyPlaceEvidence"];
    private static readonly string[] StorylineTools = ["SearchMyStorylines", "GetMyStorylineEvidence"];
    private static readonly string[] FriendTools = ["SearchMyFriends", "GetMyFriendRelationship", "AggregateMyFriendActivities"];

    public static IReadOnlyList<AssistantSkillDefinition> All { get; } = Array.AsReadOnly(new[]
    {
        Define("conversation", "问候、情绪陪伴、一般知识、改写当前文字；不查询个人数据。", []),
        Define("records", "查找本人或明确参与的经历、偏好、历史地点；不是一般闲聊。", RecordTools),
        Define("statistics", "记录数量、消费、趋势、对比等精确统计；不从搜索样本推算总体。", [.. RecordTools, "AggregateMyRecords"]),
        Define("storylines", "查询故事线、阶段、节点进度及计划状态。", [.. RecordTools, .. StorylineTools]),
        Define("friends", "查好友、关系、共同经历及共同记录排名；不推断恋人身份。", [.. RecordTools, .. FriendTools]),
        Define("shared-content", "用户明确询问好友发来的分享或继续讨论该分享时使用。", ["SearchSharedContent"]),
        Define("amap", "实时地点、天气、路线或导航；历史地点先核对个人记录。",
            [.. RecordTools, "GetNavigationTarget", "SearchAmapPlaces", "GetAmapPlaceDetails", "GeocodeAmapAddress",
                "ReverseGeocodeAmapLocation", "GetAmapWeather", "PlanAmapRoute", "MeasureAmapDistance",
                "CreateAmapNavigation", "CreateAmapTripMap"]),
        Define("conversation-summary", "只总结当前会话或用户提供的文字；不搜索数据库，不自动保存。", []),
        Define("record-summary", "依据已保存记录做日报、月度回顾或经历总结；不同于聊天摘要。",
            [.. RecordTools, .. StorylineTools, "AggregateMyRecords"]),
        Define("planning", "从当前聊天整理计划草稿或完成记录草稿；当前不支持代为写入。", []),
    });

    public static string Instructions { get; } = Read("policy.md") + "\n\n可用场景 Skill：\n" +
        string.Join('\n', All.Select(skill => $"- {skill.Key}：{skill.Description}"));

    // Any workflow or tool-policy edit invalidates old answers without relying on a manual prompt-version bump.
    public static string Version { get; } = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        Instructions + string.Join('\n', All.Select(skill =>
            $"{skill.Key}:{string.Join(',', skill.Tools)}\n{skill.Instructions}"))))).ToLowerInvariant();

    public static AssistantSkillDefinition? Find(string key) => All.FirstOrDefault(skill => skill.Key == key);

    private static AssistantSkillDefinition Define(string key, string description, string[] tools) =>
        new(key, description, Array.AsReadOnly(tools), Read($"{key}.SKILL.md"));

    private static string Read(string name)
    {
        using var stream = typeof(AssistantSkillCatalog).Assembly.GetManifestResourceStream($"AssistantSkills.{name}")
            ?? throw new InvalidOperationException("缺少应用内 AI 场景规则资源。");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
