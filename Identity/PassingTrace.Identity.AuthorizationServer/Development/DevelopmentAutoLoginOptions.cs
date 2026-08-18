namespace PassingTrace.Identity.AuthorizationServer.Development;

/// <summary>
/// 后端开发阶段的 Web 自动登录配置。仅用于本地开发，
/// 允许固定账号跳过扫码直接进入，方便前端热重载调试。
/// </summary>
public sealed class DevelopmentAutoLoginOptions
{
    public const string SectionName = "DevelopmentAutoLogin";

    /// <summary>是否启用自动登录。必须同时处于 Development（或测试）环境。</summary>
    public bool Enabled { get; init; }

    /// <summary>固定账号用户名，需符合 <c>UsernamePolicy</c> 规则。</summary>
    public string? Username { get; init; }

    /// <summary>固定账号密码，需满足 Identity 密码策略（至少 12 位）。</summary>
    public string? Password { get; init; }
}
