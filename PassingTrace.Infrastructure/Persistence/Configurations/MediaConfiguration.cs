using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PassingTrace.Core.Media;

namespace PassingTrace.Infrastructure.Persistence.Configurations;

public sealed class MediaAssetConfiguration : IEntityTypeConfiguration<MediaAsset>
{
    public void Configure(EntityTypeBuilder<MediaAsset> builder)
    {
        builder.ToTable("media_asset");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(x => x.ObjectKey).HasColumnName("object_key").HasMaxLength(1024).IsRequired();
        builder.Property(x => x.OriginalFileName).HasColumnName("original_file_name").HasMaxLength(512).IsRequired();
        builder.Property(x => x.Kind).HasColumnName("kind").HasConversion<int>().IsRequired();
        builder.Property(x => x.DeclaredMimeType).HasColumnName("declared_mime_type").HasMaxLength(255).IsRequired();
        builder.Property(x => x.VerifiedMimeType).HasColumnName("verified_mime_type").HasMaxLength(255);
        builder.Property(x => x.ExpectedSize).HasColumnName("expected_size").IsRequired();
        builder.Property(x => x.ActualSize).HasColumnName("actual_size");
        builder.Property(x => x.ExpectedSha256).HasColumnName("expected_sha256").HasMaxLength(64).IsRequired();
        builder.Property(x => x.ActualSha256).HasColumnName("actual_sha256").HasMaxLength(64);
        builder.Property(x => x.AiObjectKey).HasColumnName("ai_object_key").HasMaxLength(1024);
        builder.Property(x => x.ThumbnailObjectKey).HasColumnName("thumbnail_object_key").HasMaxLength(1024);
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        builder.Property(x => x.UploadMode).HasColumnName("upload_mode").HasConversion<int>().IsRequired();
        builder.Property(x => x.MultipartUploadId).HasColumnName("multipart_upload_id").HasMaxLength(1024);
        builder.Property(x => x.UploadExpiresAt).HasColumnName("upload_expires_at").IsRequired();
        builder.Property(x => x.ConfirmedAt).HasColumnName("confirmed_at");
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at");
        builder.Property(x => x.ProcessingError).HasColumnName("processing_error").HasMaxLength(2048);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => x.ObjectKey).IsUnique().HasDatabaseName("uk_media_asset_object_key");
        builder.HasIndex(x => new { x.UserId, x.Status, x.CreatedAt })
            .HasDatabaseName("ix_media_asset_user_status_created");
    }
}

public sealed class EventMediaAssetConfiguration : IEntityTypeConfiguration<EventMediaAsset>
{
    public void Configure(EntityTypeBuilder<EventMediaAsset> builder)
    {
        builder.ToTable("event_media_asset");
        builder.HasKey(x => new { x.EventId, x.MediaAssetId });
        builder.Property(x => x.EventId).HasColumnName("event_id");
        builder.Property(x => x.MediaAssetId).HasColumnName("media_asset_id");
        builder.Property(x => x.SortOrder).HasColumnName("sort_order").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.HasIndex(x => new { x.EventId, x.SortOrder }).IsUnique()
            .HasDatabaseName("uk_event_media_asset_order");

        builder.HasOne(x => x.Event).WithMany(x => x.MediaAssets)
            .HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.MediaAsset).WithMany(x => x.EventLinks)
            .HasForeignKey(x => x.MediaAssetId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SourceRevisionMediaConfiguration : IEntityTypeConfiguration<SourceRevisionMedia>
{
    public void Configure(EntityTypeBuilder<SourceRevisionMedia> builder)
    {
        builder.ToTable("event_source_revision_media");
        builder.HasKey(x => new { x.SourceRevisionId, x.MediaAssetId });
        builder.Property(x => x.SourceRevisionId).HasColumnName("source_revision_id");
        builder.Property(x => x.MediaAssetId).HasColumnName("media_asset_id");
        builder.Property(x => x.SortOrder).HasColumnName("sort_order").IsRequired();
        builder.HasIndex(x => new { x.SourceRevisionId, x.SortOrder }).IsUnique()
            .HasDatabaseName("uk_event_source_revision_media_order");

        builder.HasOne(x => x.SourceRevision).WithMany(x => x.MediaAssets)
            .HasForeignKey(x => x.SourceRevisionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.MediaAsset).WithMany(x => x.RevisionLinks)
            .HasForeignKey(x => x.MediaAssetId).OnDelete(DeleteBehavior.Restrict);
    }
}
