namespace PassingTrace.Events.Api.Ai.Semantics;

public sealed record EventSemanticResponse(
    long EventId,
    int SourceRevision,
    string Status,
    string? Summary,
    object? Semantic,
    string Model,
    string PipelineVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    string? Error);
