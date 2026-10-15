using Microsoft.EntityFrameworkCore;
using PassingTrace.Core.Ai;
using PassingTrace.Core.Events;
using PassingTrace.Core.Storylines;
using System.Globalization;

namespace PassingTrace.Infrastructure.Persistence.Ai;

public sealed class AiMutationRepository(TraceDbContext db) : IAiMutationRepository
{
    private IQueryable<AiMutationOperation> Owned(long userId, Guid conversationId) => db.Set<AiMutationOperation>()
        .Where(x => x.UserId == userId && x.ConversationId == conversationId &&
            db.AiConversations.Any(c => c.Id == conversationId && c.UserId == userId && c.DeletedAt == null));

    public async Task<T> ExecuteAsync<T>(string lockKey, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", cancellationToken);
                var result = await action(cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch
            {
                // A failed operation ends the request; don't leave rolled-back entities staged.
                await transaction.RollbackAsync(CancellationToken.None);
                db.ChangeTracker.Clear();
                throw;
            }
        });
    }

    public async Task LockTargetAsync(long userId, string targetType, string targetId, CancellationToken cancellationToken)
    {
        if (targetType is "Subject" or "SubjectEntry" or "SubjectRelation")
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({'s' + userId.ToString(CultureInfo.InvariantCulture)}, 0))", cancellationToken);
            foreach (var tracked in db.ChangeTracker.Entries().Where(x => x.State == EntityState.Unchanged &&
                x.Entity is Core.Subjects.Subject or Core.Subjects.SubjectEntry or Core.Subjects.SubjectRelation).ToArray())
                await tracked.ReloadAsync(cancellationToken);
            return;
        }
        if (targetType == "Storyline")
        {
            var id = Guid.Parse(targetId);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT id FROM storyline WHERE id = {id} AND user_id = {userId} FOR UPDATE", cancellationToken);
            var tracked = db.ChangeTracker.Entries<Storyline>().FirstOrDefault(x => x.Entity.Id == id && x.Entity.UserId == userId);
            if (tracked is not null) await tracked.ReloadAsync(cancellationToken);
        }
        else
        {
            var id = long.Parse(targetId, CultureInfo.InvariantCulture);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT id FROM trace_event WHERE id = {id} AND user_id = {userId} FOR UPDATE", cancellationToken);
            var tracked = db.ChangeTracker.Entries<Event>().FirstOrDefault(x => x.Entity.Id == id && x.Entity.UserId == userId);
            if (tracked is not null) await tracked.ReloadAsync(cancellationToken);
        }
    }

    public Task<bool> HasSourceMessageAsync(long userId, Guid conversationId, long messageId, CancellationToken cancellationToken) =>
        db.AiMessages.AnyAsync(x => x.Id == messageId && x.UserId == userId && x.ConversationId == conversationId &&
            x.Role == AiMessageRole.User && db.AiConversations.Any(c => c.Id == conversationId && c.UserId == userId && c.DeletedAt == null), cancellationToken);

    public Task<AiMutationOperation?> FindByKeyAsync(long userId, string key, CancellationToken cancellationToken) =>
        db.Set<AiMutationOperation>().FirstOrDefaultAsync(x => x.UserId == userId && x.OperationKey == key &&
            db.AiConversations.Any(c => c.Id == x.ConversationId && c.UserId == userId && c.DeletedAt == null), cancellationToken);

    public Task<AiMutationOperation?> FindAsync(long userId, Guid conversationId, Guid id, CancellationToken cancellationToken) =>
        Owned(userId, conversationId).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<AiMutationOperation>> ListPendingAsync(long userId, Guid conversationId, DateTimeOffset now, CancellationToken cancellationToken) =>
        await Owned(userId, conversationId).AsNoTracking().Where(x => x.State == AiMutationState.Pending && x.ExpiresAt > now)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToArrayAsync(cancellationToken);

    public void Add(AiMutationOperation operation) => db.Add(operation);
    public void Add(AiMessage message) => db.AiMessages.Add(message);
    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
