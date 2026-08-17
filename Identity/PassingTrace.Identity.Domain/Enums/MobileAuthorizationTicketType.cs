namespace PassingTrace.Identity.Domain.Enums;

/// <summary>移动端一次性授权票据的用途。</summary>
public enum MobileAuthorizationTicketType
{
    RegistrationIntent = 1,
    RegistrationHandoff = 2,
    LoginLaunch = 3
}
