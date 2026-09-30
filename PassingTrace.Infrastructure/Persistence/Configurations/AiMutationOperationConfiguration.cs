using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PassingTrace.Core.Ai;

namespace PassingTrace.Infrastructure.Persistence.Configurations;

public sealed class AiMutationOperationConfiguration : IEntityTypeConfiguration<AiMutationOperation>
{
    public void Configure(EntityTypeBuilder<AiMutationOperation> builder)
    {
        builder.ToTable("ai_mutation_operation");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.ConversationId).HasColumnName("conversation_id");
        builder.Property(x => x.SourceMessageId).HasColumnName("source_message_id");
        builder.Property(x => x.OperationKey).HasColumnName("operation_key").HasMaxLength(64);
        builder.Property(x => x.Operation).HasColumnName("operation").HasMaxLength(64);
        builder.Property(x => x.TargetType).HasColumnName("target_type").HasMaxLength(16);
        builder.Property(x => x.TargetId).HasColumnName("target_id").HasMaxLength(36);
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(512);
        builder.Property(x => x.ExpectedVersion).HasColumnName("expected_version");
        builder.Property(x => x.State).HasColumnName("state").HasConversion<int>();
        builder.Property(x => x.ResultJson).HasColumnName("result").HasColumnType("jsonb");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        builder.Property(x => x.ResolvedAt).HasColumnName("resolved_at");
        builder.HasIndex(x => new { x.UserId, x.OperationKey }).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.ConversationId, x.State, x.CreatedAt });
        builder.HasOne<AiMessage>().WithMany().HasForeignKey(x => x.SourceMessageId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<AiConversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
    }
}
