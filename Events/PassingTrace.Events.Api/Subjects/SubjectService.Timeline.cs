using System.Globalization;
using System.Text;
using System.Text.Json;
using PassingTrace.Core.Events;
using PassingTrace.Core.Subjects;

namespace PassingTrace.Events.Api.Subjects;

public sealed partial class SubjectService
{
    public async Task<SubjectTimelineResponse> TimelineAsync(long userId, Guid id, string groupBy, string? timezone,
        DateTimeOffset? from, DateTimeOffset? to, string? kind, string? state, int limit, string? cursor, CancellationToken ct)
    {
        var subject = await OwnedAsync(userId, id, ct);
        if (groupBy is not ("day" or "month")) throw new DomainValidationException("分组只能是 day 或 month。");
        var zone = Zone(timezone ?? subject.Timezone);
        var entries = await repository.TimelineEntriesAsync(userId, id, ct);
        var subjects = await repository.SubjectsAsync(userId, true, ct);
        var milestones = await repository.MilestonesAsync(userId, id, ct);
        bool AfterEnd(DateTimeOffset? at) => at is { } date && milestones.Any(x => x.Operation == "end" && x.VoidedAt == null && x.EffectiveAt < date &&
            !milestones.Any(r => r.Operation == "resume" && r.VoidedAt == null && r.EffectiveAt > x.EffectiveAt && r.EffectiveAt <= date));
        var items = new List<SubjectTimelineItem>();
        foreach (var entry in entries)
        {
            if (entry.DeletedAt != null && entry.SubjectId == id) continue;
            var at = entry.State == SubjectEntryState.Completed ? entry.HappenedAt ?? (entry.Kind == SubjectEntryKind.Record ? entry.CreatedAt : null) : entry.PlannedAt;
            var invalid = entry.DeletedAt != null;
            var origin = subjects.First(x => x.Id == entry.SubjectId);
            items.Add(new("SubjectEntry", entry.Id.ToString(), entry.Kind.ToString(), entry.State.ToString(),
                invalid ? "引用内容已删除" : entry.Title, invalid ? null : entry.Content, at, entry.CreatedAt, origin.Id,
                origin.Name, entry.SubjectId != id, invalid, AfterEnd(at), entry.Revision, invalid ? [] : Read<Guid[]>(entry.MediaIdsJson),
                invalid ? null : Read<Dictionary<string, JsonElement>>(entry.Kind == SubjectEntryKind.Plan && entry.State == SubjectEntryState.Completed ? entry.ActualFieldChangesJson : entry.FieldChangesJson),
                invalid ? null : Read<SubjectField[]>(entry.FieldDefinitionsJson)));
        }
        foreach (var evt in await repository.EventsAsync(userId, id, subject.IsSelf, ct))
        {
            var invalid = evt.DeletedAt != null;
            var at = evt.Status == EventStatus.Completed ? evt.HappenedAt ?? (evt.EventKind == EventKind.Trace ? evt.CreatedAt : null) : evt.PlannedAt;
            items.Add(new("Event", evt.Id.ToString(CultureInfo.InvariantCulture), evt.EventKind == EventKind.Trace ? "Record" : "Plan",
                evt.Status.ToString(), invalid ? "原记录已删除" : evt.Title ?? "无标题记录", invalid ? null : evt.RawContent,
                at, evt.CreatedAt, null, "原记录", !subject.IsSelf, invalid, AfterEnd(at), evt.CurrentSourceRevision,
                invalid ? [] : evt.MediaAssets.OrderBy(x => x.SortOrder).Select(x => x.MediaAssetId).ToArray()));
        }
        foreach (var milestone in milestones)
            items.Add(new("Milestone", milestone.Id.ToString(), "Milestone", milestone.VoidedAt == null ? "Completed" : "Corrected",
                milestone.Operation switch { "end" => "生命周期结束", "resume" => "生命周期恢复", _ => "纠正结束信息" },
                string.Join(" · ", new[] { milestone.Reason, milestone.Note }.Where(x => !string.IsNullOrWhiteSpace(x))),
                milestone.EffectiveAt, milestone.CreatedAt, id, subject.Name, false, false, false, subject.Revision, []));
        var ordered = items.DistinctBy(x => (x.SourceType, x.SourceId))
            .Where(x => (!from.HasValue || (x.OccurredAt ?? x.CreatedAt) >= from) && (!to.HasValue || (x.OccurredAt ?? x.CreatedAt) <= to))
            .Where(x => string.IsNullOrEmpty(kind) || x.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrEmpty(state) || x.State.Equals(state, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.OccurredAt ?? x.CreatedAt).ThenBy(x => x.SourceType, StringComparer.Ordinal).ThenBy(x => x.SourceId, StringComparer.Ordinal).ToArray();
        if (!string.IsNullOrEmpty(cursor))
        {
            try
            {
                var key = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
                var index = Array.FindIndex(ordered, x => x.SourceType + ":" + x.SourceId == key);
                if (index < 0) throw new DomainValidationException("时间轴游标已失效，请刷新。");
                ordered = ordered[(index + 1)..];
            }
            catch (FormatException) { throw new DomainValidationException("时间轴游标无效。"); }
        }
        var page = ordered.Take(Math.Clamp(limit, 1, 100)).ToArray();
        string Group(SubjectTimelineItem x) => x.OccurredAt is not { } at ? "未定日期" :
            TimeZoneInfo.ConvertTime(at, TimeZoneInfo.FindSystemTimeZoneById(zone)).ToString(groupBy == "day" ? "yyyy-MM-dd" : "yyyy-MM", CultureInfo.InvariantCulture);
        return new(page.GroupBy(Group).Select(g => new SubjectTimelineGroup(g.Key, g.ToArray())).ToArray(),
            ordered.Length > page.Length ? Convert.ToBase64String(Encoding.UTF8.GetBytes(page[^1].SourceType + ":" + page[^1].SourceId)) : null, zone);
    }
}
