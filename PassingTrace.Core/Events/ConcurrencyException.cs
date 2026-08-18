namespace PassingTrace.Core.Events;

/// <summary>
/// 乐观并发冲突：客户端携带的版本已过期，写入被拒绝，需刷新后重试。
/// </summary>
public sealed class ConcurrencyException : Exception
{
    public ConcurrencyException(string message) : base(message)
    {
    }
}
