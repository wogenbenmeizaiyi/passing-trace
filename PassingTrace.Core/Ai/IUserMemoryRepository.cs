namespace PassingTrace.Core.Ai;

public interface IUserMemoryRepository
{
    Task<IReadOnlyList<UserMemory>> ListAsync(long userId, CancellationToken cancellationToken);
    Task<UserMemory?> FindAsync(long userId, long id, CancellationToken cancellationToken);
    Task<bool> HasDuplicateAsync(long userId, long id, string fingerprint, CancellationToken cancellationToken);
    void SetEmbedding(UserMemory memory, float[]? embedding);
    Task SaveAsync(long userId, DateTimeOffset now, CancellationToken cancellationToken);
    Task RejectAllAsync(long userId, DateTimeOffset now, CancellationToken cancellationToken);
}
