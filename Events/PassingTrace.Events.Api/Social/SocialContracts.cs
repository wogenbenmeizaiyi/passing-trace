using System.ComponentModel.DataAnnotations;

namespace PassingTrace.Events.Api.Social;

public sealed record PersonProfile(string Id, string Nickname, string Bio, bool HasAvatar, string FriendCode);
public sealed record FriendView(Guid Id, PersonProfile Person, string Remark, string Label,
    string? Relationship, string? ProposedRelationship, string? RelationshipRequestedBy, Guid Version);
public sealed record FriendRequestView(Guid Id, PersonProfile Person, string Direction, string Status, DateTimeOffset CreatedAt);
public sealed record SocialPage<T>(IReadOnlyList<T> Items, string? NextCursor);
public sealed record FriendRequestInput([Required, MaxLength(100)] string Code);
public sealed record DecisionInput([Required] string Decision, Guid? Version = null);
public sealed record PreferenceInput([MaxLength(100)] string Remark, [MaxLength(30)] string Label);
public sealed record RelationshipInput([Required] string Kind, Guid Version);
public sealed record SendDirectMessageInput(Guid ClientMessageId, string Kind,
    [MaxLength(8000)] string? Text = null, long? EventId = null, Guid? StorylineId = null);
public sealed record SharedMedia(Guid Id, string Name, string Kind, string MimeType);
public sealed record SharedPlace(string Name, string? Address, decimal? Latitude, decimal? Longitude, string CoordinateSystem);
public sealed record SharedRecord(long EventId, int Revision, string Title, string? Content,
    DateTimeOffset? HappenedAt, DateTimeOffset? PlannedAt, string Status, IReadOnlyList<SharedMedia> Media,
    IReadOnlyList<string> Participants, bool Available = true, IReadOnlyList<string>? Labels = null, IReadOnlyList<SharedPlace>? Places = null);
public sealed record SharedStage(Guid Key, string Title, int Order);
public sealed record SharedNode(Guid Key, long EventId, Guid? StageKey, int Order);
public sealed record SharedEdge(Guid Source, Guid Target, string Type, string? Label);
public sealed record SharedDocument(string Kind, string Title, string? Description, string AuthorId,
    IReadOnlyList<SharedRecord> Records, IReadOnlyList<SharedStage> Stages,
    IReadOnlyList<SharedNode> Nodes, IReadOnlyList<SharedEdge> Edges,
    string? Status = null, bool Available = true);
public sealed record ShareView(Guid Id, string OwnerId, string RecipientId, SharedDocument Document, DateTimeOffset CreatedAt, bool Available);
public sealed record DirectMessageView(long Id, Guid ConversationId, string SenderId, Guid ClientMessageId,
    string Kind, string? Text, Guid? ShareId, string? ShareTitle, bool ShareAvailable, DateTimeOffset CreatedAt);
public sealed record DirectConversationView(Guid Id, Guid FriendshipId, PersonProfile Person,
    string Preview, long UnreadCount, long PeerReadThroughId, DateTimeOffset UpdatedAt, bool CanSend, long ClearedThroughId);
public sealed record ShareCardStatus(Guid Id, string Title, bool Available);
