namespace PassingTrace.Core.Ai;

public enum OutboxStatus
{
    Pending = 1,
    Processing = 2,
    Completed = 3,
    DeadLetter = 4,
    Cancelled = 5,
}

/// <summary>与 Event 写入同事务的后台任务；Worker 通过数据库租约领取。</summary>
public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public long UserId { get; set; }
    public string MessageType { get; set; } = string.Empty;
    public long? EventId { get; set; }
    public int? SourceRevision { get; set; }
    public Guid? MediaAssetId { get; set; }
    public int Priority { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public OutboxStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset AvailableAt { get; set; }
    public string? LeaseOwner { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Events.Event? Event { get; set; }
}
