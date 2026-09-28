using PassingTrace.Core.Events;

namespace PassingTrace.Core.Ai;

public sealed record FriendActivityData(long RecordCount, long PlannedCount, long UndatedCount, IReadOnlyList<Event> RecentRecords);

public interface ISocialAiQueries
{
    Task<FriendActivityData> ReadFriendActivitiesAsync(long userId, Guid friendshipId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> SearchSharesAsync(long userId, string query, Guid? friendshipId, int limit, CancellationToken cancellationToken);
}
