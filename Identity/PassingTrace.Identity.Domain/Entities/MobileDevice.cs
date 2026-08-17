namespace PassingTrace.Identity.Domain.Entities;

/// <summary>允许启动移动登录流程的个人设备凭据。</summary>
public sealed class MobileDevice
{
    public Guid Id { get; set; }

    public long UserId { get; set; }

    public required User User { get; set; }

    public required string DisplayName { get; set; }

    /// <summary>设备随机密钥的 SHA-256 Base64Url 值；服务端不保存明文密钥。</summary>
    public required string SecretHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public Guid ConcurrencyToken { get; set; }
}
