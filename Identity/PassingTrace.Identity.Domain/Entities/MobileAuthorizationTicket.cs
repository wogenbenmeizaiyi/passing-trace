using PassingTrace.Identity.Domain.Enums;

namespace PassingTrace.Identity.Domain.Entities;

/// <summary>连接 Flutter 原生流程与系统浏览器 OIDC 的短期一次性票据。</summary>
public sealed class MobileAuthorizationTicket
{
    public Guid Id { get; set; }

    /// <summary>随机票据的 SHA-256 Base64Url 值；注册意图使用随机防猜值。</summary>
    public required string TicketHash { get; set; }

    public MobileAuthorizationTicketType TicketType { get; set; }

    public long? UserId { get; set; }

    public User? User { get; set; }

    public required string ClientId { get; set; }

    public required string RedirectUri { get; set; }

    public required string CodeChallenge { get; set; }

    public string? State { get; set; }

    public string? Nonce { get; set; }

    public string? NormalizedUsernameHash { get; set; }

    public string? RequestHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? ConsumedAt { get; set; }

    public Guid ConcurrencyToken { get; set; }
}
