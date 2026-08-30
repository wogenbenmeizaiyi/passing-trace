namespace PassingTrace.Events.Api.Ai;

public sealed record RecordEvidence(
    long EventId,
    int SourceRevision,
    string? Title,
    string Snippet,
    string? SemanticSummary,
    DateTimeOffset? HappenedAt,
    DateTimeOffset CreatedAt,
    double Score);

public sealed record MemoryEvidence(
    long MemoryId,
    string Type,
    string Content,
    string Status,
    decimal Confidence,
    IReadOnlyList<long> EventIds);

public sealed record EvidenceBundle(
    IReadOnlyList<RecordEvidence> Records,
    IReadOnlyList<MemoryEvidence> Memories,
    string? Aggregate = null,
    string? TimeRange = null,
    string? Assumptions = null);

public sealed record RecordEvidenceDetail(
    long EventId,
    int SourceRevision,
    string? Title,
    string? RawContent,
    string? ImageDescriptions,
    DateTimeOffset? HappenedAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string> SemanticEvidence);
