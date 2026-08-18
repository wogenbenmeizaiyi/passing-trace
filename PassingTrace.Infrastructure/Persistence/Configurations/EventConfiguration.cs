using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PassingTrace.Core.Events;

namespace PassingTrace.Infrastructure.Persistence.Configurations;

/// <summary>定义 Event 聚合到 PostgreSQL 的表、列、索引和并发控制。</summary>
public sealed class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> builder)
    {
        builder.ToTable("trace_event", table =>
            table.HasComment("用户自由记录与计划的事实源，Trace 与 Plan 统一抽象。"));

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(e => e.EventKind)
            .HasColumnName("event_kind")
            .HasConversion<int>()
            .IsRequired();
        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .IsRequired();
        builder.Property(e => e.Title)
            .HasColumnName("title")
            .HasMaxLength(512);
        builder.Property(e => e.RawContent).HasColumnName("raw_content");
        builder.Property(e => e.HappenedAt).HasColumnName("happened_at");
        builder.Property(e => e.PlannedAt).HasColumnName("planned_at");
        builder.Property(e => e.CompletedAt).HasColumnName("completed_at");
        builder.Property(e => e.Timezone)
            .HasColumnName("timezone")
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(e => e.Visibility)
            .HasColumnName("visibility")
            .HasConversion<int>()
            .IsRequired();
        builder.Property(e => e.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(64);
        builder.Property(e => e.CurrentSourceRevision)
            .HasColumnName("current_source_revision")
            .IsRequired();
        builder.Property(e => e.ArchivedAt).HasColumnName("archived_at");
        builder.Property(e => e.DeletedAt).HasColumnName("deleted_at");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        // uint + RowVersion 会被 Npgsql 约定自动映射到 PostgreSQL 内部 xmin 列。
        builder.Property(e => e.RowVersion).IsRowVersion();

        builder.HasIndex(e => e.UserId)
            .HasDatabaseName("ix_trace_event_user_id");

        // 幂等键只在用户范围内唯一；为空时跳过（部分唯一索引）。
        builder.HasIndex(e => new { e.UserId, e.IdempotencyKey })
            .IsUnique()
            .HasFilter("idempotency_key IS NOT NULL")
            .HasDatabaseName("uk_trace_event_user_idempotency");

        // 列表查询的覆盖索引：所有权 + 删除态 + 创建时间。
        builder.HasIndex(e => new { e.UserId, e.DeletedAt, e.CreatedAt })
            .HasDatabaseName("ix_trace_event_user_created");

        builder.HasMany(e => e.SourceRevisions)
            .WithOne(s => s.Event)
            .HasForeignKey(s => s.EventId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
