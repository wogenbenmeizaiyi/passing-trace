using System.ComponentModel;
using PassingTrace.Events.Api.Ai.Evidence;
using PassingTrace.Events.Api.Ai.Mutations;
using PassingTrace.Events.Api.Subjects;

namespace PassingTrace.Events.Api.Ai.Tools.Mutations;

public sealed partial class PersonalMutationTools
{
    [Description("查询当前用户的人物、宠物和物品档案及关系。档案不是平台好友；共同出现和人物标记不推导关系或权限。名称相同必须先澄清真实 ID。")]
    public async Task<SubjectGraphResponse> QueryMySubjectsAsync(CancellationToken cancellationToken = default)
    {
        var graph = await subjects!.GraphAsync(user.UserId, cancellationToken);
        _subjectEvidence.AddRange(graph.Nodes.Select(x => new SubjectEvidence(x.Id, x.Version, x.Name)));
        return graph;
    }

    [Description("读取人物双来源时间轴，明确区分 Event 原记录、SubjectEntry 专属内容和 Milestone 生命周期。专属内容不属于我的总记录。返回同一源 ID，不复制内容。")]
    public async Task<SubjectTimelineResponse> QueryMySubjectTimelineAsync(Guid subjectId, string groupBy = "month",
        DateTimeOffset? from = null, DateTimeOffset? to = null, string? kind = null, string? state = null,
        string? cursor = null, CancellationToken cancellationToken = default)
    {
        var result = await subjects!.TimelineAsync(user.UserId, subjectId, groupBy, _calendar?.Timezone, from, to, kind, state, 50, cursor, cancellationToken);
        foreach (var item in result.Groups.SelectMany(x => x.Items).Where(x => x.SourceType == "SubjectEntry" && !x.Invalid))
            _entryEvidence.Add(new(Guid.Parse(item.SourceId), item.OriginSubjectId!.Value, item.Version, item.Title, item.Kind));
        foreach (var item in result.Groups.SelectMany(x => x.Items).Where(x => x.SourceType == "Event" && !x.Invalid))
            _timelineRecordEvidence.Add(new(long.Parse(item.SourceId, System.Globalization.CultureInfo.InvariantCulture), item.Version,
                item.Title, item.Content ?? "", null, item.OccurredAt, item.CreatedAt, 1));
        return result;
    }

    [Description("用户明确要求建人物档案时调用。必须关联已存在的档案，不能创建孤立节点。人、宠物、物品 kind 为 0/1/2。字段使用稳定 Guid，可先查询 presets。自身由系统初始化不能重复创建。")]
    public async Task<AiMutationResult> CreateMySubjectAsync(CreateSubjectRequest request, CancellationToken cancellationToken = default)
    {
        RequireIntent("create");
        return Publish(await mutations.WriteAsync(_conversationId, _messageId, "CreateMySubject", request, async (key, ct) =>
        { var result = await subjects!.CreateAsync(user.UserId, request with { Timezone = _calendar!.Timezone }, key, ct); return new[] { Target(result) }; }, cancellationToken));
    }

    [Description("查询对应类型的生活字段预设。创建时传回字段稳定 ID 及以该 ID 为键的 values；修改名称保留 ID，已有历史不可改变类型和单位。")]
    public IReadOnlyList<SubjectField> QuerySubjectFieldPresetsAsync(Core.Subjects.SubjectKind kind, string? itemType = null) => SubjectService.Presets(kind, itemType);

    [Description("按明确指令局部修改档案资料与生活字段，未指定字段保留。values 是实际变化，生成独立专属时间轴内容；不生成普通记录。自身名称只能从账号修改。")]
    public async Task<AiMutationResult> UpdateMySubjectAsync(Guid subjectId, UpdateSubjectRequest changes, CancellationToken cancellationToken = default)
    {
        RequireIntent("update");
        return Publish(await mutations.WriteAsync(_conversationId, _messageId, "UpdateMySubject", new { subjectId, changes }, async (_, ct) =>
        { var current = await subjects!.GetAsync(user.UserId, subjectId, ct); return new[] { Target(await subjects.UpdateAsync(user.UserId, subjectId, current.Version, changes, ct)) }; }, cancellationToken));
    }

    [Description("用户明确要求建立人物关系时调用。关系只有档案端点，不连接记录或计划。平台协作参与者不变。")]
    public async Task<AiMutationResult> RelateMySubjectsAsync(Guid subjectId, SubjectRelationInput relation, CancellationToken cancellationToken = default)
    {
        RequireIntent("create");
        return Publish(await mutations.WriteAsync(_conversationId, _messageId, "RelateMySubjects", new { subjectId, relation }, async (_, ct) =>
        { var current = await subjects!.GetAsync(user.UserId, subjectId, ct); var graph = await subjects.AddRelationAsync(user.UserId, subjectId, current.Version, relation, ct); return new[] { Target(graph.Nodes.Single(x => x.Id == subjectId)) }; }, cancellationToken));
    }

    [Description("明确修改关系名称、方向或起止日期。结束有效关系仍参与连通；移除误关联只能申请删除授权。")]
    public async Task<AiMutationResult> UpdateMySubjectRelationAsync(Guid relationId, UpdateSubjectRelationRequest changes, CancellationToken cancellationToken = default)
    {
        RequireIntent("update");
        return Publish(await mutations.WriteAsync(_conversationId, _messageId, "UpdateMySubjectRelation", new { relationId, changes }, async (_, ct) =>
        { var current = await subjects!.GetRelationAsync(user.UserId, relationId, ct); var updated = await subjects.UpdateRelationAsync(user.UserId, relationId, current.Revision, changes, ct); return new[] { new AiMutationTarget("SubjectRelation", relationId.ToString(), updated.Label, updated.Revision, updated.FromSubjectId) }; }, cancellationToken));
    }

    [Description("明确要求在某人物档案内追加专属记录或计划时调用。所属人物必填，可明确标记其他人物。不进入我的总记录，不同时创建 Event。kind 为 Record=0/Plan=1，计划 fieldChanges 为预期值。")]
    public async Task<AiMutationResult> CreateMySubjectEntryAsync(Guid subjectId, SubjectEntryRequest request, CancellationToken cancellationToken = default)
    {
        RequireIntent("create");
        return Publish(await mutations.WriteAsync(_conversationId, _messageId, "CreateMySubjectEntry", new { subjectId, request }, async (key, ct) =>
        { var result = await subjects!.CreateEntryAsync(user.UserId, subjectId, request with { Timezone = _calendar!.Timezone }, key, ct); return new[] { Target(result) }; }, cancellationToken));
    }

    [Description("局部修改已核实的专属内容；未传字段保留，标记仅移除展示入口。真实 ID 与当前版本由服务端核实。")]
    public async Task<AiMutationResult> UpdateMySubjectEntryAsync(Guid entryId, SubjectEntryRequest changes, CancellationToken cancellationToken = default)
    {
        RequireIntent("update");
        return Publish(await mutations.WriteAsync(_conversationId, _messageId, "UpdateMySubjectEntry", new { entryId, changes }, async (_, ct) =>
        { var current = await subjects!.GetEntryAsync(user.UserId, entryId, ct); return new[] { Target(await subjects.UpdateEntryAsync(user.UserId, entryId, current.Version, changes, ct)) }; }, cancellationToken));
    }

    [Description("明确完成或取消人物专属计划。完成必须确认实际日期和实际字段值；不能默认把预期值写成实际值。保持同一个内容 ID。")]
    public async Task<AiMutationResult> DecideMySubjectPlanAsync(Guid entryId, SubjectEntryDecision decision, CancellationToken cancellationToken = default)
    {
        RequireIntent("update");
        return Publish(await mutations.WriteAsync(_conversationId, _messageId, "UpdateMySubjectPlan", new { entryId, decision }, async (_, ct) =>
        { var current = await subjects!.GetEntryAsync(user.UserId, entryId, ct); return new[] { Target(await subjects.DecideEntryAsync(user.UserId, entryId, current.Version, decision, ct)) }; }, cancellationToken));
    }

    [Description("预览人物结束行为和所属待执行计划。引用来的计划不参与取消。默认保留所有计划，取消需要明确选择。")]
    public Task<SubjectLifecyclePreview> PreviewMySubjectLifecycleAsync(Guid subjectId, CancellationToken cancellationToken = default) => subjects!.PreviewLifecycleAsync(user.UserId, subjectId, cancellationToken);

    [Description("明确指令下结束、恢复或纠正人物生命周期。自身不可结束，离世只能纠正误标。保留专属记录，恢复不恢复已取消计划。")]
    public async Task<AiMutationResult> UpdateMySubjectLifecycleAsync(Guid subjectId, SubjectLifecycleRequest change, CancellationToken cancellationToken = default)
    {
        RequireIntent("update");
        return Publish(await mutations.WriteAsync(_conversationId, _messageId, "UpdateMySubjectLifecycle", new { subjectId, change }, async (_, ct) =>
        { var current = await subjects!.GetAsync(user.UserId, subjectId, ct); return new[] { Target(await subjects.LifecycleAsync(user.UserId, subjectId, current.Version, change, ct)) }; }, cancellationToken));
    }

    [Description("仅申请人物、专属内容或误关联删除授权。targetType 仅 Subject/SubjectEntry/SubjectRelation。确认按钮会复核归属、版本和图连通性，聊天文字不能代替授权。自身不可申请删除。")]
    public async Task<AiApprovalRequest> RequestDeleteMySubjectContentAsync(string targetType, Guid targetId, CancellationToken cancellationToken = default)
    {
        RequireIntent("delete");
        if (targetType is not ("Subject" or "SubjectEntry" or "SubjectRelation")) throw new Core.Events.DomainValidationException("删除目标类型无效。");
        return PublishApproval(await mutations.RequestDeleteAsync(_conversationId, _messageId, targetType, targetId.ToString(), cancellationToken));
    }
    private static AiMutationTarget Target(SubjectResponse result) => new("Subject", result.Id.ToString(), result.Name, result.Version);
    private static AiMutationTarget Target(SubjectEntryResponse result) => new("SubjectEntry", result.Id.ToString(), result.Title, result.Version, result.SubjectId);
}
