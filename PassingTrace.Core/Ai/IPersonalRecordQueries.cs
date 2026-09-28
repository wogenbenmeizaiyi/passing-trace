using PassingTrace.Core.Events;
using PassingTrace.Core.Storylines;

namespace PassingTrace.Core.Ai;

/// <summary>Materialized, user-scoped reads for AI tools. No query provider escapes this boundary.</summary>
public interface IPersonalRecordQueries
{
    Task<IReadOnlyList<Guid>> RankStorylinesAsync(long userId, StorylineSearchFilter filter, AiSearchOrder order, float[]? vector, CancellationToken cancellationToken);
    Task<IReadOnlyList<StorylineSearchData>> ReadStorylinesAsync(long userId, IReadOnlyList<Guid> ids, CancellationToken cancellationToken);
    Task<StorylineRevision?> FindStorylineRevisionAsync(long userId, Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<long>> RankRecordsAsync(long userId, RecordSearchFilter filter, AiSearchOrder order, float[]? vector, CancellationToken cancellationToken);
    Task<IReadOnlyList<RecordSearchData>> ReadRecordsAsync(long userId, IReadOnlyList<long> ids, Guid? participantFriendId, CancellationToken cancellationToken);
    Task<IReadOnlyList<RecordDetailData>> ReadRecordDetailsAsync(long userId, IReadOnlyList<long> ids, CancellationToken cancellationToken);
    Task<long> CountRecordsAsync(long userId, RecordStatisticsFilter filter, CancellationToken cancellationToken);
    Task<AmountTotal> SumExpensesAsync(long userId, RecordStatisticsFilter filter, string currency, CancellationToken cancellationToken);
    Task<PlanCompletion> CountPlanCompletionAsync(long userId, RecordStatisticsFilter filter, CancellationToken cancellationToken);
    Task<IReadOnlyList<MonthlyRecordCount>> CountMonthlyRecordsAsync(long userId, RecordStatisticsFilter filter, CancellationToken cancellationToken);
    Task<IReadOnlyList<UserMemory>> SearchMemoriesAsync(long userId, string query, int limit, float[]? vector, CancellationToken cancellationToken);
    Task<IReadOnlyList<SavedPlaceData>> SearchPlacesAsync(long userId, string query, IReadOnlyList<long> retrievedEventIds, int limit, CancellationToken cancellationToken);
    Task<NavigationLocation?> FindNavigationLocationAsync(long userId, long locationId, CancellationToken cancellationToken);
}

public enum AiSearchOrder { Recent, Text, Vector }
public sealed record StorylineSearchFilter(string Query, string? Category, StorylineStatus? Status);
public sealed record RecordSearchFilter(string Query, DateTimeOffset? From, DateTimeOffset? To,
    EventKind? Kind, EventStatus? Status, string? Category, string? Tag, string? Location,
    string? AdCode, decimal? CenterLatitude, decimal? CenterLongitude, int? RadiusMeters, Guid? ParticipantFriendId);
public sealed record RecordStatisticsFilter(DateTimeOffset? From, DateTimeOffset? To, string? Category, string? Tag);
public sealed record StorylineSearchData(Storyline Storyline, StorylineRevision Revision, string RetrievalText);
public sealed record RecordSearchData(Event Event, EventSearchIndex Index, IReadOnlyList<EventLabelIndex> Labels, IReadOnlyList<EventLocation> Locations);
public sealed record RecordDetailData(Event Event, EventSearchIndex Index, IReadOnlyList<SemanticMention> Mentions);
public sealed record AmountTotal(decimal? Value, long Count);
public sealed record PlanCompletion(long Completed, long Total);
public sealed record MonthlyRecordCount(int Year, int Month, long Count);
public sealed record SavedPlaceData(long LocationId, long EventId, string EventTitle, string Name, string? Address, string? AdCode, DateTimeOffset? HappenedAt);
public sealed record NavigationLocation(long EventId, long LocationId, string Name, string? Address, decimal Latitude, decimal Longitude, string? ProviderPoiId);
