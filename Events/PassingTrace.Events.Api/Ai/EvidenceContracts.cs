namespace PassingTrace.Events.Api.Ai;

public sealed record RecordEvidence(
    long EventId,
    int SourceRevision,
    string? Title,
    string Snippet,
    string? SemanticSummary,
    DateTimeOffset? HappenedAt,
    DateTimeOffset CreatedAt,
    double Score,
    IReadOnlyList<string>? Labels = null,
    string? Place = null);

public sealed record PlaceEvidence(
    long LocationId,
    long EventId,
    string EventTitle,
    string Name,
    string? Address,
    string? AdCode,
    DateTimeOffset? HappenedAt,
    int VisitCount);

public sealed record AmapPlaceEvidence(
    string CandidateId,
    string? PoiId,
    string Name,
    string? Address,
    string? Province,
    string? City,
    string? District,
    decimal Latitude,
    decimal Longitude,
    string CoordinateSystem = "GCJ02",
    string Source = "amap-live");

public sealed record AmapResultEvidence(
    string Capability,
    string Summary,
    DateTimeOffset RetrievedAt,
    string Source = "amap-live");

public sealed record AssistantAction(
    string Type,
    string Provider,
    string Label,
    string PlaceName,
    string? Address,
    decimal Latitude,
    decimal Longitude,
    string CoordinateSystem,
    string? PoiId,
    string Source,
    long? EventId = null,
    long? LocationId = null,
    string? WebUrl = null);

public sealed record MemoryEvidence(
    long MemoryId,
    string Type,
    string Content,
    string Status,
    decimal Confidence,
    IReadOnlyList<long> EventIds);

public sealed record StorylineEvidence(
    Guid StorylineId,
    int Revision,
    string Title,
    string Category,
    string Status,
    string Snippet,
    DateTimeOffset? RangeStart,
    DateTimeOffset? RangeEnd,
    IReadOnlyList<StorylineStageEvidence> Stages,
    IReadOnlyList<long> EventIds,
    double Score);

public sealed record StorylineStageEvidence(
    Guid StageKey,
    string Title,
    IReadOnlyList<string> NodeTitles);

public sealed record EvidenceBundle(
    IReadOnlyList<RecordEvidence> Records,
    IReadOnlyList<MemoryEvidence> Memories,
    string? Aggregate = null,
    string? TimeRange = null,
    string? Assumptions = null,
    IReadOnlyList<PlaceEvidence>? Places = null,
    AssistantAction? NavigationTarget = null,
    IReadOnlyList<StorylineEvidence>? Storylines = null,
    IReadOnlyList<AmapPlaceEvidence>? AmapPlaces = null,
    IReadOnlyList<AssistantAction>? Actions = null,
    IReadOnlyList<AmapResultEvidence>? AmapResults = null);

public sealed record RecordEvidenceDetail(
    long EventId,
    int SourceRevision,
    string? Title,
    string? RawContent,
    string? ImageDescriptions,
    DateTimeOffset? HappenedAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string> SemanticEvidence);
