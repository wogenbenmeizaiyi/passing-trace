using Microsoft.EntityFrameworkCore;
using PassingTrace.Core.Ai;
using PassingTrace.Core.Media;

namespace PassingTrace.Infrastructure.Persistence.Ai;

public sealed class AnalysisJobRepository(TraceDbContext db) : IAnalysisJobRepository
{
    public async Task<Guid?> ClaimAsync(string leaseOwner, DateTimeOffset now, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<Guid?>(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var message = await db.OutboxMessages.FromSqlInterpolated($$"""
                SELECT * FROM outbox_message
                WHERE status = 1
                  AND available_at <= {{now}}
                  AND (lease_expires_at IS NULL OR lease_expires_at < {{now}})
                ORDER BY priority DESC, created_at
                FOR UPDATE SKIP LOCKED
                LIMIT 1
                """).FirstOrDefaultAsync(cancellationToken);
            if (message is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return null;
            }
            message.Status = OutboxStatus.Processing;
            message.LeaseOwner = leaseOwner;
            message.LeaseExpiresAt = now.Add(leaseDuration);
            message.Attempts++;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return message.Id;
        });
    }

    public Task<OutboxMessage> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.OutboxMessages.FirstAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<MediaAsset>> FindOrphanMediaAsync(DateTimeOffset cutoff, int limit, CancellationToken cancellationToken) =>
        await db.MediaAssets.Include(x => x.EventLinks).Include(x => x.RevisionLinks)
            .Where(x => x.DeletedAt == null && x.CreatedAt < cutoff && !x.EventLinks.Any() && !x.RevisionLinks.Any())
            .Take(limit).ToListAsync(cancellationToken);

    public async Task PruneAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await db.AiMessages.Where(x => x.ExpiresAt != null && x.ExpiresAt < now).ExecuteDeleteAsync(cancellationToken);
        await db.OutboxMessages.Where(x => x.Status == OutboxStatus.Completed && x.CompletedAt < now.AddDays(-30)).ExecuteDeleteAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task BackfillAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var events = await db.Events.AsNoTracking().Where(x => x.DeletedAt == null &&
            !db.OutboxMessages.Any(job => job.EventId == x.Id && job.SourceRevision == x.CurrentSourceRevision && job.MessageType == "event.analyze"))
            .Select(x => new { x.Id, x.UserId, x.CurrentSourceRevision }).ToListAsync(cancellationToken);
        foreach (var evt in events)
            db.OutboxMessages.Add(new OutboxMessage
            {
                Id = Guid.NewGuid(),
                UserId = evt.UserId,
                EventId = evt.Id,
                SourceRevision = evt.CurrentSourceRevision,
                MessageType = "event.analyze",
                Priority = 10,
                Status = OutboxStatus.Pending,
                AvailableAt = now,
                CreatedAt = now,
            });
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
