using PassingTrace.Identity.Domain.Enums;

namespace PassingTrace.Identity.Domain.Entities;

/// <summary>由手机批准、原浏览器消费的一次性跨设备登录事务。</summary>
public sealed class QrLoginTransaction
{
    public Guid Id { get; set; }

    public required string CodeHash { get; set; }

    public required string BrowserBindingHash { get; set; }

    public required string ClientId { get; set; }

    public required string ProtectedAuthorizeRequest { get; set; }

    public QrLoginStatus Status { get; set; } = QrLoginStatus.Pending;

    public long? ApprovedUserId { get; set; }

    public User? ApprovedUser { get; set; }

    public required string SourceIp { get; set; }

    public required string UserAgent { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }

    public DateTimeOffset? RejectedAt { get; set; }

    public DateTimeOffset? ConsumedAt { get; set; }

    public Guid ConcurrencyToken { get; set; }
}
