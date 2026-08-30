using Microsoft.EntityFrameworkCore;
using PassingTrace.Core.Events;

namespace PassingTrace.Infrastructure.Persistence;

/// <summary>Event 仓储实现，通过 TraceDbContext 访问业务数据库。</summary>
public sealed class EventRepository(TraceDbContext dbContext) : IEventRepository
{
    public async Task<Event?> FindAsync(
        long userId,
        long eventId,
        CancellationToken cancellationToken)
    {
        return await dbContext.Events
            .Include(e => e.SourceRevisions)
                .ThenInclude(r => r.MediaAssets)
            .Include(e => e.MediaAssets)
                .ThenInclude(link => link.MediaAsset)
            .Include(e => e.SemanticRuns)
            .AsSplitQuery()
            .FirstOrDefaultAsync(
                e => e.Id == eventId && e.UserId == userId,
                cancellationToken);
    }

    public async Task<Event?> FindByIdempotencyKeyAsync(
        long userId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        return await dbContext.Events
            .Include(e => e.MediaAssets)
                .ThenInclude(link => link.MediaAsset)
            .AsNoTracking()
            .FirstOrDefaultAsync(
                e => e.UserId == userId && e.IdempotencyKey == idempotencyKey,
                cancellationToken);
    }

    public async Task<IReadOnlyList<Event>> ListAsync(
        EventListQuery query,
        CancellationToken cancellationToken)
    {
        var events = dbContext.Events
            .Include(e => e.MediaAssets)
                .ThenInclude(link => link.MediaAsset)
            .Include(e => e.SemanticRuns)
            .AsSplitQuery()
            .AsNoTracking()
            .Where(e => e.UserId == query.UserId);

        if (!query.IncludeDeleted)
        {
            events = events.Where(e => e.DeletedAt == null);
        }

        if (query.Kind is not null)
        {
            events = events.Where(e => e.EventKind == query.Kind);
        }

        if (query.Status is not null)
        {
            events = events.Where(e => e.Status == query.Status);
        }

        if (query.From is not null)
        {
            events = events.Where(e => e.CreatedAt >= query.From);
        }

        if (query.To is not null)
        {
            events = events.Where(e => e.CreatedAt <= query.To);
        }

        if (query.Cursor is not null)
        {
            events = events.Where(e => e.Id < query.Cursor);
        }

        return await events
            .OrderByDescending(e => e.CreatedAt)
            .ThenByDescending(e => e.Id)
            .Take(query.Limit)
            .ToListAsync(cancellationToken);
    }

    public void Add(Event evt) => dbContext.Events.Add(evt);

    public void Add(SourceRevision revision) => dbContext.SourceRevisions.Add(revision);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // xmin 并发令牌兜底，避免并发窗口内的真实冲突逃过应用层校验。
            throw new ConcurrencyException("数据已被其他请求修改，请刷新后重试。");
        }
    }
}
