using System.Text.Json;
using PassingTrace.Core.Events;
using PassingTrace.Core.Subjects;

namespace PassingTrace.Events.Api.Subjects;

public sealed partial class SubjectService
{
    public Task<SubjectEntryResponse> CreateEntryAsync(long userId, Guid subjectId, SubjectEntryRequest request, string? key, CancellationToken ct) =>
        repository.ExecuteAsync(userId, async token =>
        {
            var subject = await OwnedAsync(userId, subjectId, token);
            if (!string.IsNullOrWhiteSpace(key) && await repository.FindEntryKeyAsync(userId, key, token) is { } prior)
            {
                if (prior.SubjectId != subjectId || prior.RequestHash != Hash(request)) throw new IdempotencyConflictException(key);
                return await EntryResponseAsync(prior, token);
            }
            var entry = await AddEntryCoreAsync(userId, subject, request, key, token);
            await RebuildFieldsAsync(subject, token); Touch(subject);
            await ChangedAsync(userId, token);
            return await EntryResponseAsync(entry, token);
        }, ct);

    private async Task<SubjectEntry> AddEntryCoreAsync(long userId, Subject subject, SubjectEntryRequest request, string? key, CancellationToken ct)
    {
        var kind = request.Kind ?? SubjectEntryKind.Record;
        if (!Enum.IsDefined(kind)) throw new DomainValidationException("专属内容类型无效。");
        await media.ResolveAsync(userId, request.MediaIds, ct);
        var now = clock.GetUtcNow();
        var entry = new SubjectEntry
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            SubjectId = subject.Id,
            Kind = kind,
            State = kind == SubjectEntryKind.Record ? SubjectEntryState.Completed : SubjectEntryState.Planned,
            Title = Text(request.Title, "标题", 200),
            Content = request.Content,
            HappenedAt = request.HappenedAt?.ToUniversalTime(),
            PlannedAt = request.PlannedAt?.ToUniversalTime(),
            Timezone = Zone(request.Timezone ?? subject.Timezone),
            FieldChangesJson = Write(ValidateValues(subject, request.FieldChanges)),
            FieldDefinitionsJson = subject.FieldsJson,
            MediaIdsJson = Write(request.MediaIds ?? []),
            IdempotencyKey = key,
            RequestHash = Hash(request),
            CreatedAt = now,
            UpdatedAt = now
        };
        repository.Add(entry);
        await ReplaceReferencesAsync(userId, null, entry.Id, request.MarkedSubjectIds ?? [], subject.Id, ct);
        SaveMedia(userId, "SubjectEntry", entry.Id, entry.Revision, Read<Guid[]>(entry.MediaIdsJson));
        await repository.SaveAsync(ct);
        await SaveEntryHistoryAsync(entry, ct);
        return entry;
    }

    public async Task<SubjectEntryResponse> GetEntryAsync(long userId, Guid id, CancellationToken ct) =>
        await EntryResponseAsync(await OwnedEntryAsync(userId, id, ct), ct);
    private async Task<SubjectEntry> OwnedEntryAsync(long userId, Guid id, CancellationToken ct)
    {
        var entry = await repository.EntryAsync(userId, id, ct);
        return entry is not null && entry.DeletedAt == null ? entry : throw new KeyNotFoundException("人物专属内容不存在。");
    }

    public Task<SubjectEntryResponse> UpdateEntryAsync(long userId, Guid id, int version, SubjectEntryRequest request, CancellationToken ct) =>
        repository.ExecuteAsync(userId, async token =>
        {
            var entry = await OwnedEntryAsync(userId, id, token); Version(entry.Revision, version);
            var subject = await OwnedAsync(userId, entry.SubjectId, token);
            if (request.Kind is { } kind && kind != entry.Kind) throw new DomainValidationException("创建来源类型不能更改。");
            if (request.Title is not null) entry.Title = Text(request.Title, "标题", 200);
            if (request.Content is not null) entry.Content = request.Content;
            if (request.HappenedAt is { } actual) entry.HappenedAt = actual.ToUniversalTime();
            if (request.PlannedAt is { } planned) entry.PlannedAt = planned.ToUniversalTime();
            if (request.ClearPlannedAt) entry.PlannedAt = null;
            if (request.Timezone is not null) entry.Timezone = Zone(request.Timezone);
            if (request.FieldChanges is not null)
            {
                if (entry.Kind == SubjectEntryKind.Plan && entry.State == SubjectEntryState.Completed)
                    entry.ActualFieldChangesJson = Write(ValidateValues(subject, request.FieldChanges));
                else entry.FieldChangesJson = Write(ValidateValues(subject, request.FieldChanges));
                entry.FieldDefinitionsJson = subject.FieldsJson;
            }
            if (request.MediaIds is not null) { await media.ResolveAsync(userId, request.MediaIds, token); entry.MediaIdsJson = Write(request.MediaIds); }
            if (request.MarkedSubjectIds is not null) await ReplaceReferencesAsync(userId, null, id, request.MarkedSubjectIds, entry.SubjectId, token);
            entry.Revision++; entry.UpdatedAt = clock.GetUtcNow();
            SaveMedia(userId, "SubjectEntry", id, entry.Revision, Read<Guid[]>(entry.MediaIdsJson));
            await repository.SaveAsync(token);
            await SaveEntryHistoryAsync(entry, token);
            await RebuildFieldsAsync(subject, token); Touch(subject);
            await ChangedAsync(userId, token); return await EntryResponseAsync(entry, token);
        }, ct);

    public Task<SubjectEntryResponse> DecideEntryAsync(long userId, Guid id, int version, SubjectEntryDecision request, CancellationToken ct) =>
        repository.ExecuteAsync(userId, async token =>
        {
            var entry = await OwnedEntryAsync(userId, id, token); Version(entry.Revision, version);
            var subject = await OwnedAsync(userId, entry.SubjectId, token);
            if (entry.Kind != SubjectEntryKind.Plan || entry.State != SubjectEntryState.Planned) throw new DomainValidationException("只能处理待执行的人物计划。");
            if (request.Operation == "complete")
            {
                if (request.HappenedAt is null) throw new DomainValidationException("请确认实际发生时间。");
                if (Read<Dictionary<string, JsonElement>>(entry.FieldChangesJson).Count > 0 && request.ActualFieldChanges is null)
                    throw new DomainValidationException("请确认实际变化值，不能直接把预期值当作实际值。");
                entry.State = SubjectEntryState.Completed; entry.HappenedAt = request.HappenedAt.Value.ToUniversalTime();
                entry.CompletedAt = clock.GetUtcNow();
                entry.ActualFieldChangesJson = Write(ValidateValues(subject, request.ActualFieldChanges));
                entry.FieldDefinitionsJson = subject.FieldsJson;
            }
            else if (request.Operation == "cancel") entry.State = SubjectEntryState.Cancelled;
            else throw new DomainValidationException("请选择完成或取消计划。");
            entry.Revision++; entry.UpdatedAt = clock.GetUtcNow();
            await SaveEntryHistoryAsync(entry, token); await repository.SaveAsync(token);
            await RebuildFieldsAsync(subject, token); Touch(subject);
            await ChangedAsync(userId, token); return await EntryResponseAsync(entry, token);
        }, ct);

    public Task<bool> DeleteEntryAsync(long userId, Guid id, int version, CancellationToken ct) => repository.ExecuteAsync(userId, async token =>
    {
        var entry = await OwnedEntryAsync(userId, id, token); Version(entry.Revision, version);
        entry.DeletedAt = clock.GetUtcNow(); entry.Revision++; entry.UpdatedAt = clock.GetUtcNow();
        await SaveEntryHistoryAsync(entry, token); await repository.SaveAsync(token);
        var subject = await repository.SubjectAsync(userId, entry.SubjectId, true, token) ?? throw new KeyNotFoundException("档案不存在。");
        await RebuildFieldsAsync(subject, token); Touch(subject);
        await ChangedAsync(userId, token); return true;
    }, ct);

    private async Task<SubjectEntryResponse> EntryResponseAsync(SubjectEntry entry, CancellationToken ct)
    {
        var subject = await repository.SubjectAsync(entry.UserId, entry.SubjectId, true, ct) ?? throw new KeyNotFoundException("档案不存在。");
        var marked = (await repository.ReferencesAsync(entry.UserId, ct)).Where(x => x.EntryId == entry.Id && x.RemovedAt == null).Select(x => x.SubjectId).ToArray();
        return new(entry.Id, entry.SubjectId, subject.Name, subject.DeletedAt != null, entry.Kind, entry.State, entry.Title,
            entry.Content, entry.HappenedAt, entry.PlannedAt, entry.CompletedAt, entry.Timezone, entry.Revision,
            Read<Dictionary<string, JsonElement>>(entry.FieldChangesJson), Read<Dictionary<string, JsonElement>>(entry.ActualFieldChangesJson),
            Read<SubjectField[]>(entry.FieldDefinitionsJson), Read<Guid[]>(entry.MediaIdsJson), marked, entry.CreatedAt, entry.UpdatedAt);
    }

    private async Task SaveEntryHistoryAsync(SubjectEntry entry, CancellationToken ct) => History(entry.UserId, "SubjectEntry", entry.Id, entry.Revision,
        new { Entry = entry, MarkedSubjectIds = (await repository.ReferencesAsync(entry.UserId, ct)).Where(x => x.EntryId == entry.Id && x.RemovedAt == null).Select(x => x.SubjectId).ToArray() });

    internal async Task ReplaceReferencesAsync(long userId, long? eventId, Guid? entryId, IReadOnlyList<Guid> markedIds, Guid? originId, CancellationToken ct)
    {
        if (markedIds.Count > 100 || markedIds.Distinct().Count() != markedIds.Count) throw new DomainValidationException("人物标记最多 100 个且不能重复。");
        var ids = markedIds.Where(x => x != originId).ToHashSet();
        var existing = (await repository.ReferencesAsync(userId, ct)).Where(x => x.EventId == eventId && x.EntryId == entryId && x.RemovedAt == null).ToArray();
        foreach (var id in ids.Where(id => existing.All(x => x.SubjectId != id))) await OwnedAsync(userId, id, ct);
        foreach (var reference in existing.Where(x => !ids.Contains(x.SubjectId))) reference.RemovedAt = clock.GetUtcNow();
        foreach (var id in ids.Where(id => existing.All(x => x.SubjectId != id))) repository.Add(new SubjectTimelineReference
        { Id = Guid.NewGuid(), UserId = userId, SubjectId = id, EventId = eventId, EntryId = entryId, CreatedAt = clock.GetUtcNow() });
    }
}
