using Microsoft.EntityFrameworkCore;
using PassingTrace.Core.Ai;
using Pgvector;

namespace PassingTrace.Infrastructure.Persistence.Ai;

public sealed class UserMemoryRepository(TraceDbContext db) : IUserMemoryRepository
{
    public async Task<IReadOnlyList<UserMemory>> ListAsync(long userId, CancellationToken cancellationToken) =>
        await db.UserMemories.AsNoTracking().Include(x => x.Evidence)
            .Where(x => x.UserId == userId && x.Status != UserMemoryStatus.Rejected)
            .OrderByDescending(x => x.Status == UserMemoryStatus.Corrected)
            .ThenByDescending(x => x.Status == UserMemoryStatus.Confirmed).ThenByDescending(x => x.UpdatedAt)
            .ToListAsync(cancellationToken);

    public Task<UserMemory?> FindAsync(long userId, long id, CancellationToken cancellationToken) =>
        db.UserMemories.Include(x => x.Evidence).FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, cancellationToken);

    public Task<bool> HasDuplicateAsync(long userId, long id, string fingerprint, CancellationToken cancellationToken) =>
        db.UserMemories.AnyAsync(x => x.UserId == userId && x.Id != id && x.Fingerprint == fingerprint, cancellationToken);

    public void SetEmbedding(UserMemory memory, float[]? embedding) =>
        db.Entry(memory).Property<Vector?>("Embedding").CurrentValue = embedding is null ? null : new Vector(embedding);

    public async Task SaveAsync(long userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await new AnalysisOutbox(db).IncrementWatermarkAsync(userId, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RejectAllAsync(long userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.UserMemories.Where(x => x.UserId == userId && x.Status != UserMemoryStatus.Rejected)
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.Status, UserMemoryStatus.Rejected)
                    .SetProperty(x => x.RejectedAt, now).SetProperty(x => x.UpdatedAt, now), cancellationToken);
            await SaveAsync(userId, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }
}
