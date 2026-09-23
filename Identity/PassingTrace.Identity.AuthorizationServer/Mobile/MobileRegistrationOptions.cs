namespace PassingTrace.Identity.AuthorizationServer.Mobile;

public sealed class MobileRegistrationOptions
{
    public const string SectionName = "MobileRegistration";

    public int TicketLifetimeSeconds { get; init; } = 120;
}
