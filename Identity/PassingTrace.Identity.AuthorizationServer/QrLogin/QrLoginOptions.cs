namespace PassingTrace.Identity.AuthorizationServer.QrLogin;

public sealed class QrLoginOptions
{
    public const string SectionName = "QrLogin";
    public int LifetimeSeconds { get; init; } = 120;
    public int PollIntervalSeconds { get; init; } = 2;
    public string? PublicOrigin { get; init; }
}
