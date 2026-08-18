using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PassingTrace.Core.Events;

namespace PassingTrace.Infrastructure.Persistence.Configurations;

/// <summary>定义 SourceRevision 快照的表、列和唯一性约束。</summary>
public sealed class SourceRevisionConfiguration : IEntityTypeConfiguration<SourceRevision>
{
    public void Configure(EntityTypeBuilder<SourceRevision> builder)
    {
        builder.ToTable("event_source_revision", table =>
            table.HasComment("Event 的 Source 修订快照，旧值永不原地覆盖。"));

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(s => s.EventId).HasColumnName("event_id").IsRequired();
        builder.Property(s => s.Revision).HasColumnName("revision").IsRequired();
        builder.Property(s => s.Title)
            .HasColumnName("title")
            .HasMaxLength(512);
        builder.Property(s => s.RawContent).HasColumnName("raw_content");
        builder.Property(s => s.HappenedAt).HasColumnName("happened_at");
        builder.Property(s => s.PlannedAt).HasColumnName("planned_at");
        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(s => new { s.EventId, s.Revision })
            .IsUnique()
            .HasDatabaseName("uk_event_source_revision_event_revision");
    }
}
