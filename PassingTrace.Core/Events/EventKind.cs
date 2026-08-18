namespace PassingTrace.Core.Events;

/// <summary>
/// Event 的创建来源，创建后不可变更。Plan 完成后仍保持 Plan，
/// 通过 <see cref="EventStatus.Completed"/> 与发生时间进入历史时间线。
/// </summary>
public enum EventKind
{
    /// <summary>用户直接记录已经发生或正在发生的事情。</summary>
    Trace = 0,

    /// <summary>用户以未来计划形式创建。</summary>
    Plan = 1,
}
