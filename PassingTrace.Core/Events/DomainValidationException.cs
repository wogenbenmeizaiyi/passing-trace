namespace PassingTrace.Core.Events;

/// <summary>
/// 领域校验失败，例如 Event 的标题与原文同时为空。
/// </summary>
public sealed class DomainValidationException : Exception
{
    public DomainValidationException(string message) : base(message)
    {
    }
}
