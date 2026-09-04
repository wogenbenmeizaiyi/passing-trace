namespace PassingTrace.Events.Api.Ai;

public sealed record CreateConversationRequest(string? Title);
public sealed record SendAssistantMessageRequest(string Content);
public sealed record AiConversationResponse(Guid Id, string Title, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record AiConversationDetailResponse(
    Guid Id,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<AiMessageResponse> Messages);
public sealed record AiMessageResponse(long Id, string Role, string Content, DateTimeOffset CreatedAt, object? Evidence);

public sealed record AssistantStreamEvent(string Type, object? Data);

public sealed record UserMemoryResponse(
    long Id,
    string Type,
    string Content,
    decimal Confidence,
    string Status,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<long> EvidenceEventIds);
public sealed record UpdateUserMemoryRequest(string? Content, string? Type, string? Status);

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
