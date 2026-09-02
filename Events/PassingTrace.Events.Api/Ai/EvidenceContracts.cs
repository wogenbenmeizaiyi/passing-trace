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
    object? NavigationTarget = null,
    IReadOnlyList<StorylineEvidence>? Storylines = null);

public sealed record RecordEvidenceDetail(
    long EventId,
    int SourceRevision,
    string? Title,
    string? RawContent,
    string? ImageDescriptions,
    DateTimeOffset? HappenedAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string> SemanticEvidence);
