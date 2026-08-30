using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PassingTrace.Core.Ai;

namespace PassingTrace.Infrastructure.Persistence.Configurations;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_message");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.MessageType).HasColumnName("message_type").HasMaxLength(128);
        builder.Property(x => x.EventId).HasColumnName("event_id");
        builder.Property(x => x.SourceRevision).HasColumnName("source_revision");
        builder.Property(x => x.MediaAssetId).HasColumnName("media_asset_id");
        builder.Property(x => x.Priority).HasColumnName("priority");
        builder.Property(x => x.PayloadJson).HasColumnName("payload").HasColumnType("jsonb");
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>();
        builder.Property(x => x.Attempts).HasColumnName("attempts");
        builder.Property(x => x.AvailableAt).HasColumnName("available_at");
        builder.Property(x => x.LeaseOwner).HasColumnName("lease_owner").HasMaxLength(256);
        builder.Property(x => x.LeaseExpiresAt).HasColumnName("lease_expires_at");
        builder.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(4096);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.CompletedAt).HasColumnName("completed_at");
        builder.HasIndex(x => new { x.Status, x.AvailableAt, x.Priority })
            .HasDatabaseName("ix_outbox_claim");
        builder.HasIndex(x => new { x.EventId, x.SourceRevision, x.MessageType })
            .HasDatabaseName("ix_outbox_event_revision_type");
        builder.HasOne(x => x.Event).WithMany().HasForeignKey(x => x.EventId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
