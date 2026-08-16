namespace PassingTrace.Identity.Application.Models
{
    /// <summary>
    /// 登录结果，携带登录是否成功以及成功时签发的访问令牌。
    /// </summary>
    /// <param name="Status">登录结果状态。</param>
    /// <param name="AccessToken">登录成功时签发的访问令牌；失败时为 null。</param>
    public sealed record LoginResult(LoginStatus Status, AccessToken? AccessToken)
    {
        /// <summary>登录成功，返回签发的访问令牌。</summary>
        public static LoginResult Succeeded(AccessToken accessToken) =>
            new(LoginStatus.Succeeded, accessToken);

        /// <summary>登录失败，用户名/邮箱或密码不正确。</summary>
        public static LoginResult InvalidCredentials() =>
            new(LoginStatus.InvalidCredentials, null);

        /// <summary>登录失败，账号已被禁用。</summary>
        public static LoginResult Inactive() =>
            new(LoginStatus.Inactive, null);
    }

    /// <summary>
    /// 登录结果状态。
    /// </summary>
    public enum LoginStatus
    {
        /// <summary>登录成功。</summary>
        Succeeded,

        /// <summary>登录失败，账号或密码错误。</summary>
        InvalidCredentials,

        /// <summary>登录失败，账号未启用（已禁用）。</summary>
        Inactive
    }


}
