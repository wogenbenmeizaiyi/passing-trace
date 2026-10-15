using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PassingTrace.Core.Subjects;

namespace PassingTrace.Infrastructure.Persistence.Configurations;

public sealed class SubjectConfiguration : IEntityTypeConfiguration<Subject>
{
    public void Configure(EntityTypeBuilder<Subject> b)
    {
        b.ToTable("subject", t => t.HasCheckConstraint("ck_subject_self", "NOT \"IsSelf\" OR (\"DeletedAt\" IS NULL AND \"State\" = 0 AND \"Kind\" = 0)"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Revision).IsConcurrencyToken();
        b.Property(x => x.Name).HasMaxLength(200);
        b.HasIndex(x => x.UserId).IsUnique().HasFilter("\"IsSelf\" = TRUE");
        b.HasIndex(x => new { x.UserId, x.IdempotencyKey }).IsUnique().HasFilter("\"IdempotencyKey\" IS NOT NULL");
        b.HasIndex(x => new { x.UserId, x.DeletedAt });
    }
}

public sealed class SubjectRelationConfiguration : IEntityTypeConfiguration<SubjectRelation>
{
    public void Configure(EntityTypeBuilder<SubjectRelation> b)
    {
        b.ToTable("subject_relation"); b.HasKey(x => x.Id);
        b.Property(x => x.Revision).IsConcurrencyToken();
        b.Property(x => x.Label).HasMaxLength(100);
        b.HasIndex(x => x.UserId);
        b.HasOne<Subject>().WithMany().HasForeignKey(x => x.FromSubjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Subject>().WithMany().HasForeignKey(x => x.ToSubjectId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SubjectEntryConfiguration : IEntityTypeConfiguration<SubjectEntry>
{
    public void Configure(EntityTypeBuilder<SubjectEntry> b)
    {
        b.ToTable("subject_entry"); b.HasKey(x => x.Id);
        b.Property(x => x.Revision).IsConcurrencyToken();
        b.Property(x => x.Title).HasMaxLength(200);
        b.HasOne<Subject>().WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.UserId, x.SubjectId, x.CreatedAt });
        b.HasIndex(x => new { x.UserId, x.IdempotencyKey }).IsUnique().HasFilter("\"IdempotencyKey\" IS NOT NULL");
    }
}

public sealed class SubjectReferenceConfiguration : IEntityTypeConfiguration<SubjectTimelineReference>
{
    public void Configure(EntityTypeBuilder<SubjectTimelineReference> b)
    {
        b.ToTable("subject_timeline_reference", t => t.HasCheckConstraint("ck_subject_reference_source", "(\"EventId\" IS NULL) <> (\"EntryId\" IS NULL)"));
        b.HasKey(x => x.Id);
        b.HasOne<Subject>().WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Event).WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<SubjectEntry>().WithMany().HasForeignKey(x => x.EntryId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.UserId, x.SubjectId, x.EventId }).IsUnique().HasFilter("\"EventId\" IS NOT NULL AND \"RemovedAt\" IS NULL");
        b.HasIndex(x => new { x.UserId, x.SubjectId, x.EntryId }).IsUnique().HasFilter("\"EntryId\" IS NOT NULL AND \"RemovedAt\" IS NULL");
    }
}

public sealed class SubjectHistoryConfiguration : IEntityTypeConfiguration<SubjectHistory>
{
    public void Configure(EntityTypeBuilder<SubjectHistory> b)
    {
        b.ToTable("subject_history"); b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.UserId, x.TargetType, x.TargetId, x.Revision }).IsUnique();
    }
}

public sealed class SubjectMilestoneConfiguration : IEntityTypeConfiguration<SubjectMilestone>
{
    public void Configure(EntityTypeBuilder<SubjectMilestone> b)
    {
        b.ToTable("subject_milestone"); b.HasKey(x => x.Id);
        b.HasOne<Subject>().WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.UserId, x.SubjectId, x.EffectiveAt });
    }
}

public sealed class SubjectMediaReferenceConfiguration : IEntityTypeConfiguration<SubjectMediaReference>
{
    public void Configure(EntityTypeBuilder<SubjectMediaReference> b)
    {
        b.ToTable("subject_media_reference"); b.HasKey(x => x.Id);
        b.HasOne<Core.Media.MediaAsset>().WithMany().HasForeignKey(x => x.MediaId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.UserId, x.TargetType, x.TargetId, x.MediaId, x.Revision }).IsUnique();
        b.HasIndex(x => x.MediaId);
    }
}
