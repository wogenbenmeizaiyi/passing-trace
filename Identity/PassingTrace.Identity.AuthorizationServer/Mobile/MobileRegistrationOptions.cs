namespace PassingTrace.Identity.AuthorizationServer.Mobile;

public sealed class MobileRegistrationOptions
{
    public const string SectionName = "MobileRegistration";

    /// <summary>个人部署的一次性初始化口令；应由环境变量或 Secret 注入。</summary>
    public string BootstrapCode { get; init; } = string.Empty;

    public int MaxUsers { get; init; } = 1;

    public int TicketLifetimeSeconds { get; init; } = 120;
}
