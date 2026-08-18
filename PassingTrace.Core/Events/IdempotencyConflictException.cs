namespace PassingTrace.Core.Events;

/// <summary>
/// 同一幂等键与不同请求内容冲突，必须拒绝而不是静默复用已有 Event。
/// </summary>
public sealed class IdempotencyConflictException : Exception
{
    public IdempotencyConflictException(string idempotencyKey)
        : base($"幂等键 '{idempotencyKey}' 已与不同的请求内容关联。")
    {
    }
}
