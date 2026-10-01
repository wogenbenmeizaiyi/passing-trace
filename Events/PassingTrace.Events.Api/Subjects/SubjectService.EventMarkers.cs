using PassingTrace.Core.Events;

namespace PassingTrace.Events.Api.Subjects;

public sealed partial class SubjectService
{
    public Task<T> ExecuteAsync<T>(long userId, Func<CancellationToken, Task<T>> action, CancellationToken ct) =>
        repository.ExecuteAsync(userId, action, ct);

    public async Task ApplyAsync(Event evt, SourceRevision revision, IReadOnlyList<Guid>? subjectIds, CancellationToken ct)
    {
        var ids = subjectIds ?? Read<Guid[]>(evt.SubjectIdsJson);
        if (ids.Count > 100 || ids.Distinct().Count() != ids.Count)
            throw new DomainValidationException("人物标记最多 100 个且不能重复。");
        var old = Read<Guid[]>(evt.SubjectIdsJson).ToHashSet();
        foreach (var id in ids.Where(id => !old.Contains(id))) await OwnedAsync(evt.UserId, id, ct);
        if (evt.Id == 0)
        {
            foreach (var id in ids) repository.Add(new Core.Subjects.SubjectTimelineReference
            { Id = Guid.NewGuid(), UserId = evt.UserId, SubjectId = id, Event = evt, CreatedAt = clock.GetUtcNow() });
        }
        else if (subjectIds is not null)
            await ReplaceReferencesAsync(evt.UserId, evt.Id, null, ids, null, ct);
        evt.SubjectIdsJson = Write(ids);
        revision.SubjectIdsJson = evt.SubjectIdsJson;
    }
}
