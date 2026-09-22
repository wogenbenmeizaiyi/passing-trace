namespace PassingTrace.Core.Social;

/// <summary>A new friendship gets a new identity; old grants never revive on re-add.</summary>
public sealed class Friendship
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long FirstUserId { get; set; }
    public long SecondUserId { get; set; }
    public bool Active { get; set; } = true;
    public string? Relationship { get; set; }
    public string? ProposedRelationship { get; set; }
    public long? RelationshipRequestedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}

public sealed class FriendPreference
{
    public Guid FriendshipId { get; set; }
    public long UserId { get; set; }
    public string Remark { get; set; } = "";
    public string Label { get; set; } = "朋友";
}

public sealed class FriendRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long SenderId { get; set; }
    public long RecipientId { get; set; }
    public string Status { get; set; } = "pending";
    public DateTimeOffset CreatedAt { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}

public sealed class UserBlock
{
    public long UserId { get; set; }
    public long BlockedUserId { get; set; }
}

public sealed class EventParticipant
{
    public long EventId { get; set; }
    public long UserId { get; set; }
    public Guid FriendshipId { get; set; }
    public bool Active { get; set; } = true;
    public bool Declined { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public Events.Event Event { get; set; } = null!;
}

public sealed class DirectConversation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FriendshipId { get; set; }
    public long FirstUserId { get; set; }
    public long SecondUserId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ConversationMember
{
    public Guid ConversationId { get; set; }
    public long UserId { get; set; }
    public long ReadThroughId { get; set; }
    public long ClearedThroughId { get; set; }
}

public sealed class DirectMessage
{
    public long Id { get; set; }
    public Guid ConversationId { get; set; }
    public long SenderId { get; set; }
    public Guid ClientMessageId { get; set; }
    public string Kind { get; set; } = "text";
    public string? Text { get; set; }
    public Guid? ShareId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ContentShare
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FriendshipId { get; set; }
    public long OwnerId { get; set; }
    public long RecipientId { get; set; }
    public string Kind { get; set; } = "record";
    public long? EventId { get; set; }
    public Guid? StorylineId { get; set; }
    public int Revision { get; set; }
    public string ContentJson { get; set; } = "{}";
    public string SearchText { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

/// <summary>Durable, per-user notification log. SSE is only a delivery mechanism.</summary>
public sealed class SocialNotification
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Kind { get; set; } = "";
    public string Text { get; set; } = "";
    public string? Target { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool Read { get; set; }
}
