namespace PassingTrace.Core.Subjects;

public interface ISubjectMediaQueries
{
    Task<bool> HasReferenceAsync(long userId, Guid mediaId, CancellationToken ct);
}
