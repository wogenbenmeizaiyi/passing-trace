namespace PassingTrace.Core.Ai;

public interface IAiEvidenceQueries
{
    Task<AiEvidenceAccess> ReadEvidenceAccessAsync(long userId, IReadOnlyList<long> eventIds, IReadOnlyList<Guid> shareIds, CancellationToken cancellationToken);
}

public sealed record AiEvidenceAccess(IReadOnlyList<Guid> FriendshipIds, IReadOnlyList<long> EventIds, IReadOnlyList<Guid> ShareIds);
public sealed record AiConversationHeader(Guid Id, string Title, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record AiConversationCursor(DateTimeOffset UpdatedAt, Guid Id);
public sealed record AiMessageQuery(long? BeforeId = null, long AfterId = 0, int? Limit = null,
    bool NewestFirst = false, DateTimeOffset? NotExpiredAt = null);

public interface IAiConversationRepository : IAiEvidenceQueries
{
    Task<IReadOnlyList<AiConversationHeader>> ListAsync(long userId, int? limit, AiConversationCursor? cursor, CancellationToken cancellationToken);
    Task<AiConversationHeader?> ReadHeaderAsync(long userId, Guid id, CancellationToken cancellationToken);
    Task<AiConversation?> FindAsync(long userId, Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<AiMessage>> ReadMessagesAsync(long userId, Guid conversationId, AiMessageQuery query, CancellationToken cancellationToken);
    Task<ConversationSummary?> FindSummaryAsync(long userId, Guid conversationId, CancellationToken cancellationToken);
    Task<long> ReadWatermarkAsync(long userId, CancellationToken cancellationToken);
    Task<bool> HasSocialHistoryAsync(long userId, CancellationToken cancellationToken);
    Task<bool> HasEarlierAnswerAsync(long userId, Guid conversationId, long beforeId, CancellationToken cancellationToken);
    Task<string?> ReadLatestQuestionAsync(long userId, Guid conversationId, long beforeId, CancellationToken cancellationToken);
    Task<bool> TryUpdateTitleAsync(AiConversation conversation, string expectedTitle, string title, CancellationToken cancellationToken);
    void Add(AiConversation conversation);
    void Add(AiMessage message);
    void Add(ConversationSummary summary);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
