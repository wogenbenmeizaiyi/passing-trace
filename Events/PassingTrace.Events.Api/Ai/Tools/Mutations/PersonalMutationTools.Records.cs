using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using PassingTrace.Core.Events;
using PassingTrace.Events.Api.Ai.Mutations;
using PassingTrace.Events.Api.Events;

namespace PassingTrace.Events.Api.Ai.Tools.Mutations;

public sealed partial class PersonalMutationTools
{
    [Description("按用户本轮明确的创建或保存指令创建私人经历记录或计划。普通自述、总结或建议不可调用。")]
    public async Task<AiMutationResult> CreateMyRecordAsync(
        [Required, RegularExpression("^(Trace|Plan)$")] string kind,
        [Required, StringLength(512, MinimumLength = 1)] string title,
        [MaxLength(8000)] string? rawContent = null,
        [DataType(DataType.DateTime)] DateTimeOffset? happenedAt = null,
        [DataType(DataType.DateTime)] DateTimeOffset? plannedAt = null,
        [Description("只传用户明确标记的人物档案 ID；不会添加关系或协作参与者。")] IReadOnlyList<Guid>? subjectIds = null,
        CancellationToken cancellationToken = default)
    {
        RequireIntent("create");
        if (!Enum.TryParse<EventKind>(kind, out var parsed)) throw new DomainValidationException("记录类型不合法。");
        if (parsed == EventKind.Trace && plannedAt is not null || parsed == EventKind.Plan && happenedAt is not null)
            throw new DomainValidationException("经历使用发生时间，计划使用计划时间。");
        var command = new CreateEventCommand(user.UserId, parsed, title.Trim(), rawContent?.Trim(),
            happenedAt?.ToUniversalTime(), plannedAt?.ToUniversalTime(), _calendar!.Timezone, null, SubjectIds: subjectIds);
        return Publish(await mutations.WriteAsync(_conversationId, _messageId, "CreateMyRecord", command, async (key, ct) =>
        {
            var evt = await eventService.CreateAsync(command with { IdempotencyKey = key }, ct);
            return new[] { AiMutationService.RecordTarget(evt) };
        }, cancellationToken));
    }

    [Description("按明确指令局部编辑本人记录或计划。只提供要修改的字段；清空字段使用 clearFields（title/rawContent/happenedAt/plannedAt）。不改类型、附件或参与者。")]
    public async Task<AiMutationResult> UpdateMyRecordAsync([Range(1, long.MaxValue)] long eventId,
        [Required] AiRecordPatch changes, CancellationToken cancellationToken = default)
    {
        RequireIntent("update");
        var clear = (changes.ClearFields ?? []).ToHashSet(StringComparer.Ordinal);
        if (clear.Except(["title", "rawContent", "happenedAt", "plannedAt"]).Any()) throw new DomainValidationException("未知的清空字段。");
        if (changes.Title is null && changes.RawContent is null && changes.HappenedAt is null && changes.PlannedAt is null && changes.SubjectIds is null && clear.Count == 0)
            throw new DomainValidationException("没有指定修改内容。");
        if (clear.Contains("title") && changes.Title is not null || clear.Contains("rawContent") && changes.RawContent is not null ||
            clear.Contains("happenedAt") && changes.HappenedAt is not null || clear.Contains("plannedAt") && changes.PlannedAt is not null)
            throw new DomainValidationException("同一字段不能同时修改和清空。");
        return Publish(await mutations.WriteAsync(_conversationId, _messageId, "UpdateMyRecord", new { eventId, changes }, async (_, ct) =>
        {
            var evt = await mutations.ReadRecordAsync(eventId, ct);
            if (evt.EventKind == EventKind.Plan && changes.HappenedAt is not null || evt.EventKind == EventKind.Trace && changes.PlannedAt is not null)
                throw new DomainValidationException("经历使用发生时间，计划使用计划时间。");
            var updated = await eventService.UpdateSourceAsync(new(user.UserId, eventId, evt.RowVersion,
                clear.Contains("title") ? null : changes.Title?.Trim() ?? evt.Title,
                clear.Contains("rawContent") ? null : changes.RawContent?.Trim() ?? evt.RawContent,
                clear.Contains("happenedAt") ? null : changes.HappenedAt ?? evt.HappenedAt,
                clear.Contains("plannedAt") ? null : changes.PlannedAt ?? evt.PlannedAt,
                evt.Timezone, evt.MediaAssets.OrderBy(x => x.SortOrder).Select(x => x.MediaAssetId).ToArray(), SubjectIds: changes.SubjectIds), ct);
            return new[] { AiMutationService.RecordTarget(updated) };
        }, cancellationToken));
    }
}
