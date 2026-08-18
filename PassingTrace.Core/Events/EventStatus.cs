namespace PassingTrace.Core.Events;

/// <summary>
/// Event 的生命周期状态。归档不是业务状态，用 archived_at 表示。
/// </summary>
public enum EventStatus
{
    /// <summary>计划待执行。</summary>
    Planned = 0,

    /// <summary>已经发生或计划已完成。</summary>
    Completed = 1,

    /// <summary>计划已取消。</summary>
    Cancelled = 2,
}
