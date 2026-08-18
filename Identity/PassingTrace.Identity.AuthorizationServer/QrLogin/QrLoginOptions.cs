namespace PassingTrace.Identity.AuthorizationServer.QrLogin;

public sealed class QrLoginOptions
{
    public const string SectionName = "QrLogin";
    public int LifetimeSeconds { get; init; } = 120;
    public int PollIntervalSeconds { get; init; } = 2;
    public string? PublicOrigin { get; init; }

    /// <summary>
    /// 仅 Development：开启后，新建的 QR 扫码事务会被指定用户自动批准，
    /// 浏览器无需手机扫码即可完成登录。生产环境必须保持 false。
    /// </summary>
    public bool DevAutoApprove { get; init; } = false;

    /// <summary>
    /// 仅 Development：DevAutoApprove=true 时使用，指定替哪个用户自动批准。
    /// </summary>
    public string? DevApproveUsername { get; init; }
}
