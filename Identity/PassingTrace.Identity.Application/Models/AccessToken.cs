namespace PassingTrace.Identity.Application.Models
{
    /// <summary>
    /// 访问令牌（JWT），登录成功后由服务端签发并返回给客户端。
    /// </summary>
    /// <param name="Token">JWT 令牌字符串，客户端在后续请求中通过 Authorization 头携带。</param>
    /// <param name="ExpiresAt">令牌过期时间（UTC），过期后需要重新登录或刷新令牌。</param>
    public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

}
