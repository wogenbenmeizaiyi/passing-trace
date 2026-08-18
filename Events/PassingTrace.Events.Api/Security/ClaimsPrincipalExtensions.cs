using System.Security.Claims;

namespace PassingTrace.Events.Api.Security;

/// <summary>从已验证的 ClaimsPrincipal 中解析业务用户键。</summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// 从 OpenIddict 签发的 Access Token 的 <c>sub</c> 声明解析 user_id。
    /// 客户端传入的 user_id 不可信，此处只信任协议声明。
    /// </summary>
    public static long GetUserId(this ClaimsPrincipal principal)
    {
        var subject = principal.FindFirst("sub")?.Value;

        if (!long.TryParse(subject, out var userId))
        {
            throw new UnauthorizedAccessException(
                "访问令牌缺少有效的 sub 声明。");
        }

        return userId;
    }
}
