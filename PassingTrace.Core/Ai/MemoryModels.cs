namespace PassingTrace.Core.Ai;

public enum UserMemoryStatus
{
    Automatic = 1,
    Confirmed = 2,
    Corrected = 3,
    Rejected = 4,
}

public enum UserMemoryType
{
    Preference = 1,
    Profile = 2,
    Habit = 3,
    Goal = 4,
    Constraint = 5,
    Other = 99,
}

public sealed class UserMemory
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public UserMemoryType Type { get; set; }
    public string Content { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public UserMemoryStatus Status { get; set; }
    public string Fingerprint { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? RejectedAt { get; set; }

    public List<UserMemoryEvidence> Evidence { get; set; } = [];
}

public sealed class UserMemoryEvidence
{
    public long Id { get; set; }
    public long UserMemoryId { get; set; }
    public long EventId { get; set; }
    public int SourceRevision { get; set; }
    public long SemanticRunId { get; set; }
    public string EvidenceJson { get; set; } = "{}";
    public UserMemory UserMemory { get; set; } = null!;
}

public sealed class UserDataWatermark
{
    public long UserId { get; set; }
    public long Version { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AiConversation
{
    public Guid Id { get; set; }
    public long UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public List<AiMessage> Messages { get; set; } = [];
    public ConversationSummary? Summary { get; set; }
}

public enum AiMessageRole
{
    User = 1,
    Assistant = 2,
    System = 3,
}

public sealed class AiMessage
{
    public long Id { get; set; }
    public Guid ConversationId { get; set; }
    public long UserId { get; set; }
    public AiMessageRole Role { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? EvidenceSnapshotJson { get; set; }
    public string? Model { get; set; }
    public string? PromptVersion { get; set; }
    public long? DataWatermark { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public AiConversation Conversation { get; set; } = null!;
}

public sealed class ConversationSummary
{
    public Guid ConversationId { get; set; }
    public long UserId { get; set; }
    public string Content { get; set; } = string.Empty;
    public long ThroughMessageId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public AiConversation Conversation { get; set; } = null!;
}
