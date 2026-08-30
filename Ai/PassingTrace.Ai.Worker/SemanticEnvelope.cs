namespace PassingTrace.Ai.Worker;

public sealed record SemanticEnvelope(
    string Summary,
    IReadOnlyList<ImageDescription> Images,
    IReadOnlyList<SemanticMentionData> Mentions,
    IReadOnlyList<ExpenseFactData> Expenses,
    IReadOnlyList<MemoryCandidate> Memories);

public sealed record ImageDescription(Guid MediaId, string Description);

public sealed record SemanticMentionData(
    string Category,
    string NormalizedValue,
    string OriginalValue,
    string Assertion,
    decimal Confidence,
    int? TextStart,
    int? TextLength,
    Guid? MediaId);

public sealed record ExpenseFactData(
    decimal Amount,
    string Currency,
    string Purpose,
    string Scope,
    decimal Confidence,
    string Evidence);

public sealed record MemoryCandidate(
    string Type,
    string Content,
    decimal Confidence,
    string Evidence);
