namespace PassingTrace.Core.Events;

/// <summary>
/// 目标 Event 不存在或不属于当前用户。为避免枚举他人资源，调用方应统一返回 404。
/// </summary>
public sealed class EventNotFoundException : Exception
{
    public EventNotFoundException(long userId, long eventId)
        : base($"未找到用户 {userId} 的事件 {eventId}。")
    {
    }
}
