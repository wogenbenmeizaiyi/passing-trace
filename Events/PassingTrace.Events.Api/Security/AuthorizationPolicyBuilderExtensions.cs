using Microsoft.AspNetCore.Authorization;

namespace PassingTrace.Events.Api.Security;

/// <summary>授权策略扩展，校验 OpenIddict 签发的 scope 声明。</summary>
public static class AuthorizationPolicyBuilderExtensions
{
    /// <summary>
    /// 要求 Token 的 scope 声明包含指定 Scope。OpenIddict 的 scope 声明为
    /// 空格分隔的字符串，因此需要拆分后精确匹配。
    /// </summary>
    public static AuthorizationPolicyBuilder RequireScope(
        this AuthorizationPolicyBuilder builder,
        string scope)
    {
        return builder.RequireAssertion(context =>
        {
            var scopeClaim = context.User.FindFirst("scope")?.Value;
            return scopeClaim is not null &&
                scopeClaim.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Contains(scope, StringComparer.Ordinal);
        });
    }
}
