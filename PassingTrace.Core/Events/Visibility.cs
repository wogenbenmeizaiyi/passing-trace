namespace PassingTrace.Core.Events;

/// <summary>
/// Event 的可见性。V1 只支持 private，其余枚举预留，不代表已经实现社交可见性。
/// </summary>
public enum Visibility
{
    /// <summary>仅本人可见，V1 唯一支持的取值。</summary>
    Private = 0,
}
