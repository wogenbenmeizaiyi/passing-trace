using PassingTrace.Core.Events;

namespace PassingTrace.Core.Subjects;

/// <summary>Explicit dossier markers are independent of social participants.</summary>
public interface IEventSubjectService
{
    Task<T> ExecuteAsync<T>(long userId, Func<CancellationToken, Task<T>> action, CancellationToken ct);
    Task ApplyAsync(Event evt, SourceRevision revision, IReadOnlyList<Guid>? subjectIds, CancellationToken ct);
}
