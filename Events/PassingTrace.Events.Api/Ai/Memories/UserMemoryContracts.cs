namespace PassingTrace.Events.Api.Ai.Memories;

public sealed record UserMemoryResponse(
    long Id,
    string Type,
    string Content,
    decimal Confidence,
    string Status,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<long> EvidenceEventIds);
public sealed record UpdateUserMemoryRequest(string? Content, string? Type, string? Status);
