namespace PassingTrace.Events.Api.Common;

/// <summary>
/// 请求缺少 If-Match 版本条件，调用方应返回 428 Precondition Required。
/// </summary>
public sealed class PreconditionRequiredException : Exception
{
    public PreconditionRequiredException()
        : base("缺少 If-Match 版本条件。")
    {
    }
}
