using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PassingTrace.Core.Ai;
using Pgvector;

namespace PassingTrace.Infrastructure.Persistence.Configurations;

public sealed class UserMemoryConfiguration : IEntityTypeConfiguration<UserMemory>
{
    public void Configure(EntityTypeBuilder<UserMemory> builder)
    {
        builder.ToTable("user_memory");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.Type).HasColumnName("memory_type").HasConversion<int>();
        builder.Property(x => x.Content).HasColumnName("content");
        builder.Property(x => x.Confidence).HasColumnName("confidence").HasPrecision(5, 4);
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>();
        builder.Property(x => x.Fingerprint).HasColumnName("fingerprint").HasMaxLength(64);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.RejectedAt).HasColumnName("rejected_at");
        builder.Property<Vector?>("Embedding").HasColumnName("embedding").HasColumnType("vector(1024)");
        builder.HasIndex(x => new { x.UserId, x.Fingerprint }).IsUnique()
            .HasDatabaseName("uk_user_memory_user_fingerprint");
        builder.HasIndex(x => new { x.UserId, x.Status, x.UpdatedAt })
            .HasDatabaseName("ix_user_memory_user_status_updated");
        builder.HasIndex("Embedding").HasMethod("hnsw").HasOperators("vector_cosine_ops")
            .HasDatabaseName("ix_user_memory_embedding");
    }
}

public sealed class UserMemoryEvidenceConfiguration : IEntityTypeConfiguration<UserMemoryEvidence>
{
    public void Configure(EntityTypeBuilder<UserMemoryEvidence> builder)
    {
        builder.ToTable("user_memory_evidence");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(x => x.UserMemoryId).HasColumnName("user_memory_id");
        builder.Property(x => x.EventId).HasColumnName("event_id");
        builder.Property(x => x.SourceRevision).HasColumnName("source_revision");
        builder.Property(x => x.SemanticRunId).HasColumnName("semantic_run_id");
        builder.Property(x => x.EvidenceJson).HasColumnName("evidence").HasColumnType("jsonb");
        builder.HasIndex(x => new { x.UserMemoryId, x.EventId, x.SourceRevision }).IsUnique()
            .HasDatabaseName("uk_user_memory_evidence_source");
        builder.HasOne(x => x.UserMemory).WithMany(x => x.Evidence)
            .HasForeignKey(x => x.UserMemoryId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class UserDataWatermarkConfiguration : IEntityTypeConfiguration<UserDataWatermark>
{
    public void Configure(EntityTypeBuilder<UserDataWatermark> builder)
    {
        builder.ToTable("user_data_watermark");
        builder.HasKey(x => x.UserId);
        builder.Property(x => x.UserId).HasColumnName("user_id").ValueGeneratedNever();
        builder.Property(x => x.Version).HasColumnName("version");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
    }
}

public sealed class AiConversationConfiguration : IEntityTypeConfiguration<AiConversation>
{
    public void Configure(EntityTypeBuilder<AiConversation> builder)
    {
        builder.ToTable("ai_conversation");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(256);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at");
        builder.HasIndex(x => new { x.UserId, x.DeletedAt, x.UpdatedAt })
            .HasDatabaseName("ix_ai_conversation_user_updated");
    }
}

public sealed class AiMessageConfiguration : IEntityTypeConfiguration<AiMessage>
{
    public void Configure(EntityTypeBuilder<AiMessage> builder)
    {
        builder.ToTable("ai_message");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(x => x.ConversationId).HasColumnName("conversation_id");
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.Role).HasColumnName("role").HasConversion<int>();
        builder.Property(x => x.Content).HasColumnName("content");
        builder.Property(x => x.EvidenceSnapshotJson).HasColumnName("evidence_snapshot").HasColumnType("jsonb");
        builder.Property(x => x.Model).HasColumnName("model").HasMaxLength(128);
        builder.Property(x => x.PromptVersion).HasColumnName("prompt_version").HasMaxLength(64);
        builder.Property(x => x.DataWatermark).HasColumnName("data_watermark");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        builder.HasIndex(x => new { x.UserId, x.ConversationId, x.Id })
            .HasDatabaseName("ix_ai_message_user_conversation");
        builder.HasOne(x => x.Conversation).WithMany(x => x.Messages)
            .HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ConversationSummaryConfiguration : IEntityTypeConfiguration<ConversationSummary>
{
    public void Configure(EntityTypeBuilder<ConversationSummary> builder)
    {
        builder.ToTable("conversation_summary");
        builder.HasKey(x => x.ConversationId);
        builder.Property(x => x.ConversationId).HasColumnName("conversation_id").ValueGeneratedNever();
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.Content).HasColumnName("content");
        builder.Property(x => x.ThroughMessageId).HasColumnName("through_message_id");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.HasOne(x => x.Conversation).WithOne(x => x.Summary)
            .HasForeignKey<ConversationSummary>(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
    }
}
