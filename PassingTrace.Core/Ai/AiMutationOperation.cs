namespace PassingTrace.Core.Ai;

public enum AiMutationState { Succeeded, Pending, Cancelled, Expired, Conflict }

/// <summary>A durable receipt or a single, user-confirmed deletion request.</summary>
public sealed class AiMutationOperation
{
    public Guid Id { get; set; }
    public long UserId { get; set; }
    public Guid ConversationId { get; set; }
    public long SourceMessageId { get; set; }
    public string OperationKey { get; set; } = "";
    public string Operation { get; set; } = "";
    public string TargetType { get; set; } = "";
    public string? TargetId { get; set; }
    public string Title { get; set; } = "";
    public uint? ExpectedVersion { get; set; }
    public AiMutationState State { get; set; }
    public string? ResultJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}

public interface IAiMutationRepository
{
    /// <summary>Serialize one operation and atomically commit its business changes and receipt.</summary>
    Task<T> ExecuteAsync<T>(string lockKey, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken);
    /// <summary>Lock the owned deletion target until the enclosing operation commits.</summary>
    Task LockTargetAsync(long userId, string targetType, string targetId, CancellationToken cancellationToken);
    Task<bool> HasSourceMessageAsync(long userId, Guid conversationId, long messageId, CancellationToken cancellationToken);
    Task<AiMutationOperation?> FindByKeyAsync(long userId, string key, CancellationToken cancellationToken);
    Task<AiMutationOperation?> FindAsync(long userId, Guid conversationId, Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<AiMutationOperation>> ListPendingAsync(long userId, Guid conversationId, DateTimeOffset now, CancellationToken cancellationToken);
    void Add(AiMutationOperation operation);
    void Add(AiMessage message);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
