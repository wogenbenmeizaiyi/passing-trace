using PassingTrace.Core.Media;

namespace PassingTrace.Core.Ai;

public interface IAnalysisJobRepository
{
    Task<Guid?> ClaimAsync(string leaseOwner, DateTimeOffset now, TimeSpan leaseDuration, CancellationToken cancellationToken);
    Task<OutboxMessage> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<MediaAsset>> FindOrphanMediaAsync(DateTimeOffset cutoff, int limit, CancellationToken cancellationToken);
    Task PruneAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task BackfillAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
