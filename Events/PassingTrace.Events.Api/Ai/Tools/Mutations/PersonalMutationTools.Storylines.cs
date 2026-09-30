using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using PassingTrace.Core.Events;
using PassingTrace.Core.Storylines;
using PassingTrace.Events.Api.Ai.Mutations;
using PassingTrace.Events.Api.Storylines;

namespace PassingTrace.Events.Api.Ai.Tools.Mutations;

public sealed partial class PersonalMutationTools
{
    [Description("创建本人故事线。有序节点只能引用本人已有记录或提供 newPlan（两者选一），顺序关系由应用生成；不接受画布坐标或复杂连线。")]
    public async Task<AiMutationResult> CreateMyStorylineAsync(
        [Required, StringLength(120, MinimumLength = 1)] string title,
        [Required, MaxLength(500)] IReadOnlyList<AiStorylineNodeInput> nodes,
        [MaxLength(2000)] string? description = null,
        [RegularExpression("^(trip|activity|project|challenge|lifecycle|series|life-period|other)$")] string categoryKey = "other",
        [RegularExpression("^(Ongoing|Completed)$")] string status = "Ongoing",
        CancellationToken cancellationToken = default)
    {
        RequireIntent("create");
        if (!Enum.TryParse<StorylineStatus>(status, out var parsed)) throw new DomainValidationException("故事线状态不合法。");
        foreach (var node in nodes)
            if (node.EventId.HasValue == (node.NewPlan is not null)) throw new DomainValidationException("节点必须选择已有记录或新计划之一。");
        return Publish(await mutations.WriteAsync(_conversationId, _messageId, "CreateMyStoryline", new { title = title.Trim(), nodes, description, categoryKey, status }, async (key, ct) =>
        {
            var mapped = nodes.Select((x, i) => new StorylineNodeInput(StableGuid(key, $"node:{i}"),
                x.EventId.HasValue ? "existing-event" : "new-plan", x.EventId, null,
                x.NewPlan is null ? null : new InlinePlanInput(x.NewPlan.Title, x.NewPlan.PlannedAt, x.NewPlan.RawContent, _calendar!.Timezone), null, i)).ToArray();
            var edges = mapped.Zip(mapped.Skip(1), (a, b) => new StorylineEdgeInput(StableGuid(key, $"edge:{a.SemanticOrder}"), a.Key, b.Key, StorylineRelationType.Sequence, null)).ToArray();
            var saved = await storylines.CreateAsync(user.UserId, new(title, description, categoryKey, parsed, null, null, null, mapped, edges, null), key, ct);
            return StoryTargets(saved);
        }, cancellationToken));
    }

    [Description("局部编辑已核实的本人故事线。一次执行一种基础信息或节点操作，复用现有图校验；不直接编辑复杂连线，移除节点不会删除记录。")]
    public async Task<AiMutationResult> UpdateMyStorylineAsync(Guid storylineId,
        [Required] AiStorylineChange changes, CancellationToken cancellationToken = default)
    {
        RequireIntent("update");
        return Publish(await mutations.WriteAsync(_conversationId, _messageId, "UpdateMyStoryline", new { storylineId, changes }, async (key, ct) =>
        {
            var current = await storylines.GetAsync(user.UserId, storylineId, null, ct);
            if (changes.Status is not null && !Enum.TryParse<StorylineStatus>(changes.Status, out _)) throw new DomainValidationException("故事线状态不合法。");
            if (changes.Operation == "update-metadata" && changes.Title is null && changes.Description is null && changes.CategoryKey is null && changes.Status is null)
                throw new DomainValidationException("没有指定修改内容。");
            var change = new StorylineChangeRequest(changes.Operation,
                changes.Operation is "add-existing-event" or "add-plan" ? StableGuid(key, "new-node") : changes.NodeKey,
                changes.EventId, null,
                changes.NewPlan is null ? null : new InlinePlanInput(changes.NewPlan.Title, changes.NewPlan.PlannedAt, changes.NewPlan.RawContent, _calendar!.Timezone),
                changes.StageKey, changes.SemanticOrder, ParentNodeKey: changes.ParentNodeKey,
                Title: changes.Title, Description: changes.Description, CategoryKey: changes.CategoryKey,
                Status: changes.Status is null ? null : Enum.Parse<StorylineStatus>(changes.Status));
            var saved = await storylines.ApplyChangeAsync(user.UserId, storylineId, current.Version, change, key, ct);
            return StoryTargets(saved);
        }, cancellationToken));
    }

    private static IReadOnlyList<AiMutationTarget> StoryTargets(StorylineSaveResponse saved) =>
        new[] { new AiMutationTarget("Storyline", saved.Storyline.Id.ToString(), saved.Storyline.Title, saved.Storyline.Revision) }
            .Concat(saved.CreatedPlans.Values.Distinct().Select(id =>
            {
                var node = saved.Storyline.Nodes.Single(x => x.EventId == id);
                return new AiMutationTarget("Plan", id.ToString(System.Globalization.CultureInfo.InvariantCulture), node.Title, node.SourceRevision);
            })).ToArray();

    private static Guid StableGuid(string key, string suffix) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"{key}:{suffix}"))[..16]);
}
