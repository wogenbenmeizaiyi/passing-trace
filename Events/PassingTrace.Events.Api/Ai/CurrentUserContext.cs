using PassingTrace.Events.Api.Security;

namespace PassingTrace.Events.Api.Ai;

/// <summary>从当前请求 JWT sub 解析用户；任何 Agent Tool 都不接受 userId 参数。</summary>
public sealed class CurrentUserContext(IHttpContextAccessor accessor)
{
    public long UserId => accessor.HttpContext?.User.GetUserId()
        ?? throw new InvalidOperationException("当前请求没有已认证用户上下文。");
}
