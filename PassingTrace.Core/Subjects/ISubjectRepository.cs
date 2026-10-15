using PassingTrace.Core.Events;

namespace PassingTrace.Core.Subjects;

/// <summary>User-scoped materialized reads; transactions and graph locks remain in Infrastructure.</summary>
public interface ISubjectRepository
{
    Task<T> ExecuteAsync<T>(long userId, Func<CancellationToken, Task<T>> action, CancellationToken ct);
    Task<IReadOnlyList<Subject>> SubjectsAsync(long userId, bool includeDeleted, CancellationToken ct);
    Task<Subject?> SelfAsync(long userId, CancellationToken ct);
    Task<Subject?> SubjectAsync(long userId, Guid id, bool includeDeleted, CancellationToken ct);
    Task<IReadOnlyList<SubjectRelation>> RelationsAsync(long userId, CancellationToken ct);
    Task<SubjectRelation?> RelationAsync(long userId, Guid id, CancellationToken ct);
    Task<IReadOnlyList<SubjectEntry>> EntriesAsync(long userId, Guid? subjectId, CancellationToken ct);
    Task<IReadOnlyList<SubjectEntry>> TimelineEntriesAsync(long userId, Guid subjectId, CancellationToken ct);
    Task<SubjectEntry?> EntryAsync(long userId, Guid id, CancellationToken ct);
    Task<IReadOnlyList<SubjectTimelineReference>> ReferencesAsync(long userId, CancellationToken ct);
    Task<IReadOnlyList<Event>> EventsAsync(long userId, Guid subjectId, bool isSelf, CancellationToken ct);
    Task<IReadOnlyList<SubjectMilestone>> MilestonesAsync(long userId, Guid subjectId, CancellationToken ct);
    Task<IReadOnlyList<SubjectHistory>> HistoryAsync(long userId, string type, Guid id, CancellationToken ct);
    Task<Subject?> FindSubjectKeyAsync(long userId, string key, CancellationToken ct);
    Task<SubjectEntry?> FindEntryKeyAsync(long userId, string key, CancellationToken ct);
    void Add(Subject subject);
    void Add(SubjectRelation relation);
    void Add(SubjectEntry entry);
    void Add(SubjectTimelineReference reference);
    void Add(SubjectMilestone milestone);
    void Add(SubjectHistory history);
    void Add(SubjectMediaReference media);
    Task SaveAsync(CancellationToken ct);
}
