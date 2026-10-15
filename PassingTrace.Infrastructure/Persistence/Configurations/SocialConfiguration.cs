using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PassingTrace.Core.Social;

namespace PassingTrace.Infrastructure.Persistence.Configurations;

public sealed class SocialConfiguration : IEntityTypeConfiguration<Friendship>,
    IEntityTypeConfiguration<FriendPreference>, IEntityTypeConfiguration<FriendRequest>,
    IEntityTypeConfiguration<UserBlock>, IEntityTypeConfiguration<EventParticipant>,
    IEntityTypeConfiguration<DirectConversation>, IEntityTypeConfiguration<ConversationMember>,
    IEntityTypeConfiguration<DirectMessage>, IEntityTypeConfiguration<ContentShare>,
    IEntityTypeConfiguration<SocialNotification>
{
    public void Configure(EntityTypeBuilder<Friendship> b)
    {
        b.ToTable("social_friendship"); b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.FirstUserId, x.SecondUserId }).IsUnique().HasFilter("\"Active\" = true");
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Property(x => x.Relationship).HasMaxLength(30);
        b.Property(x => x.ProposedRelationship).HasMaxLength(30);
    }
    public void Configure(EntityTypeBuilder<FriendPreference> b)
    {
        b.ToTable("social_friend_preference"); b.HasKey(x => new { x.FriendshipId, x.UserId });
        b.Property(x => x.Remark).HasMaxLength(100); b.Property(x => x.Label).HasMaxLength(30);
    }
    public void Configure(EntityTypeBuilder<FriendRequest> b)
    {
        b.ToTable("social_friend_request"); b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.SenderId, x.RecipientId }).IsUnique().HasFilter("\"Status\" = 'pending'");
        b.Property(x => x.Status).HasMaxLength(20); b.Property(x => x.Version).IsConcurrencyToken();
    }
    public void Configure(EntityTypeBuilder<UserBlock> b)
    { b.ToTable("social_user_block"); b.HasKey(x => new { x.UserId, x.BlockedUserId }); }
    public void Configure(EntityTypeBuilder<EventParticipant> b)
    {
        b.ToTable("social_event_participant"); b.HasKey(x => new { x.EventId, x.UserId });
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => new { x.UserId, x.Active, x.EventId });
        b.HasOne(x => x.Event).WithMany(x => x.Participants).HasForeignKey(x => x.EventId);
    }
    public void Configure(EntityTypeBuilder<DirectConversation> b)
    {
        b.ToTable("social_conversation"); b.HasKey(x => x.Id);
        b.HasIndex(x => x.FriendshipId).IsUnique();
        b.HasIndex(x => new { x.FirstUserId, x.UpdatedAt });
        b.HasIndex(x => new { x.SecondUserId, x.UpdatedAt });
    }
    public void Configure(EntityTypeBuilder<ConversationMember> b)
    { b.ToTable("social_conversation_member"); b.HasKey(x => new { x.ConversationId, x.UserId }); }
    public void Configure(EntityTypeBuilder<DirectMessage> b)
    {
        b.ToTable("social_message"); b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.ConversationId, x.Id });
        b.HasIndex(x => new { x.SenderId, x.ClientMessageId }).IsUnique();
        b.Property(x => x.Text).HasMaxLength(8000); b.Property(x => x.Kind).HasMaxLength(20);
    }
    public void Configure(EntityTypeBuilder<ContentShare> b)
    {
        b.ToTable("social_share"); b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.RecipientId, x.CreatedAt });
        b.HasIndex(x => new { x.OwnerId, x.EventId });
    }
    public void Configure(EntityTypeBuilder<SocialNotification> b)
    {
        b.ToTable("social_notification"); b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.UserId, x.Id }); b.Property(x => x.Text).HasMaxLength(300);
        b.Property(x => x.Kind).HasMaxLength(40); b.Property(x => x.Target).HasMaxLength(200);
    }
}
