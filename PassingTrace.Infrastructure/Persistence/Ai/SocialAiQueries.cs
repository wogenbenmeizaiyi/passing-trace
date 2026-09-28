using Microsoft.EntityFrameworkCore;
using PassingTrace.Core.Ai;
using PassingTrace.Core.Events;

namespace PassingTrace.Infrastructure.Persistence.Ai;

internal static class SocialRecordAccess
{
    internal static IQueryable<Event> ReadableEvents(TraceDbContext db, long userId) => db.Events.Where(e => e.DeletedAt == null &&
        (e.UserId == userId || db.EventParticipants.Any(p => p.EventId == e.Id && p.UserId == userId && p.Active &&
            db.Friendships.Any(f => f.Id == p.FriendshipId && f.Active &&
                ((f.FirstUserId == userId && f.SecondUserId == e.UserId) || (f.SecondUserId == userId && f.FirstUserId == e.UserId))))));
}

public sealed class SocialAiQueries(TraceDbContext db) : ISocialAiQueries
{
    public async Task<FriendActivityData> ReadFriendActivitiesAsync(long userId, Guid friendshipId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var friendship = await db.Friendships.AsNoTracking().SingleOrDefaultAsync(x => x.Id == friendshipId && x.Active &&
            (x.FirstUserId == userId || x.SecondUserId == userId), cancellationToken);
        if (friendship is null) return new(0, 0, 0, []);
        var other = friendship.FirstUserId == userId ? friendship.SecondUserId : friendship.FirstUserId;
        var query = SocialRecordAccess.ReadableEvents(db, userId).AsNoTracking().Where(e => e.UserId == other ||
            db.EventParticipants.Any(p => p.EventId == e.Id && p.UserId == other && p.Active && db.Friendships.Any(f => f.Id == p.FriendshipId && f.Active)));
        var completed = query.Where(e => e.Status == EventStatus.Completed && e.HappenedAt >= from && e.HappenedAt <= to);
        var count = await completed.LongCountAsync(cancellationToken);
        var recent = await completed.OrderByDescending(x => x.HappenedAt).ThenByDescending(x => x.Id).Take(5).ToListAsync(cancellationToken);
        var planned = await query.LongCountAsync(e => e.Status == EventStatus.Planned && e.PlannedAt >= from && e.PlannedAt <= to, cancellationToken);
        var undated = await query.LongCountAsync(e => e.Status == EventStatus.Completed && e.HappenedAt == null, cancellationToken);
        return new(count, planned, undated, recent);
    }

    public async Task<IReadOnlyList<Guid>> SearchSharesAsync(long userId, string query, Guid? friendshipId, int limit, CancellationToken cancellationToken)
    {
        var shares = db.ContentShares.AsNoTracking().Where(x => x.RecipientId == userId && x.RevokedAt == null &&
            db.Friendships.Any(f => f.Id == x.FriendshipId && f.Active));
        if (friendshipId is not null) shares = shares.Where(x => x.FriendshipId == friendshipId);
        if (!string.IsNullOrWhiteSpace(query)) shares = shares.Where(x => x.SearchText.Contains(query));
        return await shares.OrderByDescending(x => x.CreatedAt).Take(Math.Clamp(limit, 1, 20)).Select(x => x.Id).ToListAsync(cancellationToken);
    }
}
