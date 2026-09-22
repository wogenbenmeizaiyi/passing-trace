using Microsoft.EntityFrameworkCore;
using PassingTrace.Core.Ai;
using PassingTrace.Core.Events;
using PassingTrace.Core.Media;
using PassingTrace.Core.Storylines;
using PassingTrace.Core.Social;

namespace PassingTrace.Infrastructure;

/// <summary>
/// 业务数据工作单元，管理 Event 与 SourceRevision 的持久化。
/// 与 Identity 使用不同的数据库，业务表之间不再拆库。
/// </summary>
public sealed class TraceDbContext(DbContextOptions<TraceDbContext> options)
    : DbContext(options)
{
    public DbSet<Event> Events => Set<Event>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<FriendPreference> FriendPreferences => Set<FriendPreference>();
    public DbSet<FriendRequest> FriendRequests => Set<FriendRequest>();
    public DbSet<UserBlock> UserBlocks => Set<UserBlock>();
    public DbSet<EventParticipant> EventParticipants => Set<EventParticipant>();
    public DbSet<DirectConversation> DirectConversations => Set<DirectConversation>();
    public DbSet<ConversationMember> ConversationMembers => Set<ConversationMember>();
    public DbSet<DirectMessage> DirectMessages => Set<DirectMessage>();
    public DbSet<ContentShare> ContentShares => Set<ContentShare>();
    public DbSet<SocialNotification> SocialNotifications => Set<SocialNotification>();

    public DbSet<SourceRevision> SourceRevisions => Set<SourceRevision>();

    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<EventMediaAsset> EventMediaAssets => Set<EventMediaAsset>();
    public DbSet<SourceRevisionMedia> SourceRevisionMedia => Set<SourceRevisionMedia>();
    public DbSet<EventSemanticRun> EventSemanticRuns => Set<EventSemanticRun>();
    public DbSet<SemanticMention> SemanticMentions => Set<SemanticMention>();
    public DbSet<ExpenseFact> ExpenseFacts => Set<ExpenseFact>();
    public DbSet<EventSearchIndex> EventSearchIndexes => Set<EventSearchIndex>();
    public DbSet<UserMemory> UserMemories => Set<UserMemory>();
    public DbSet<UserMemoryEvidence> UserMemoryEvidence => Set<UserMemoryEvidence>();
    public DbSet<UserDataWatermark> UserDataWatermarks => Set<UserDataWatermark>();
    public DbSet<AiConversation> AiConversations => Set<AiConversation>();
    public DbSet<AiMessage> AiMessages => Set<AiMessage>();
    public DbSet<ConversationSummary> ConversationSummaries => Set<ConversationSummary>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<SourceRevisionLabel> SourceRevisionLabels => Set<SourceRevisionLabel>();
    public DbSet<EventLabelIndex> EventLabelIndexes => Set<EventLabelIndex>();
    public DbSet<EventLocation> EventLocations => Set<EventLocation>();
    public DbSet<UserPlace> UserPlaces => Set<UserPlace>();
    public DbSet<Storyline> Storylines => Set<Storyline>();
    public DbSet<StorylineRevision> StorylineRevisions => Set<StorylineRevision>();
    public DbSet<StorylineStage> StorylineStages => Set<StorylineStage>();
    public DbSet<StorylineNode> StorylineNodes => Set<StorylineNode>();
    public DbSet<StorylineEdge> StorylineEdges => Set<StorylineEdge>();
    public DbSet<StorylineRevisionTag> StorylineRevisionTags => Set<StorylineRevisionTag>();
    public DbSet<StorylineWebLayout> StorylineWebLayouts => Set<StorylineWebLayout>();
    public DbSet<StorylineWebNodeLayout> StorylineWebNodeLayouts => Set<StorylineWebNodeLayout>();
    public DbSet<StorylineWebStageLayout> StorylineWebStageLayouts => Set<StorylineWebStageLayout>();
    public DbSet<StorylineSearchIndex> StorylineSearchIndexes => Set<StorylineSearchIndex>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasPostgresExtension("vector");
        builder.HasPostgresExtension("pg_trgm");
        builder.ApplyConfigurationsFromAssembly(typeof(TraceDbContext).Assembly);
    }
}
