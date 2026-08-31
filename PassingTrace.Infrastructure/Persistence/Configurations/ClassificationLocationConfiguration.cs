using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PassingTrace.Core.Events;
using Pgvector;

namespace PassingTrace.Infrastructure.Persistence.Configurations;

public sealed class SourceRevisionLabelConfiguration : IEntityTypeConfiguration<SourceRevisionLabel>
{
    public void Configure(EntityTypeBuilder<SourceRevisionLabel> builder)
    {
        builder.ToTable("source_revision_label");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(x => x.SourceRevisionId).HasColumnName("source_revision_id");
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.Type).HasColumnName("label_type").HasConversion<int>();
        builder.Property(x => x.Decision).HasColumnName("decision").HasConversion<int>();
        builder.Property(x => x.TaxonomyKey).HasColumnName("taxonomy_key").HasMaxLength(64);
        builder.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(128);
        builder.Property(x => x.NormalizedValue).HasColumnName("normalized_value").HasMaxLength(128);
        builder.Property(x => x.SortOrder).HasColumnName("sort_order");
        builder.HasIndex(x => new { x.UserId, x.NormalizedValue }).HasDatabaseName("ix_source_label_user_value");
        builder.HasOne(x => x.SourceRevision).WithMany(x => x.Labels)
            .HasForeignKey(x => x.SourceRevisionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class EventLabelIndexConfiguration : IEntityTypeConfiguration<EventLabelIndex>
{
    public void Configure(EntityTypeBuilder<EventLabelIndex> builder)
    {
        builder.ToTable("event_label_index");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.EventId).HasColumnName("event_id");
        builder.Property(x => x.SourceRevision).HasColumnName("source_revision");
        builder.Property(x => x.SemanticRunId).HasColumnName("semantic_run_id");
        builder.Property(x => x.Type).HasColumnName("label_type").HasConversion<int>();
        builder.Property(x => x.Origin).HasColumnName("origin").HasConversion<int>();
        builder.Property(x => x.TaxonomyKey).HasColumnName("taxonomy_key").HasMaxLength(64);
        builder.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(128);
        builder.Property(x => x.NormalizedValue).HasColumnName("normalized_value").HasMaxLength(128);
        builder.Property(x => x.Confidence).HasColumnName("confidence").HasPrecision(5, 4);
        builder.Property(x => x.IsCurrent).HasColumnName("is_current");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.HasIndex(x => new { x.UserId, x.Type, x.TaxonomyKey, x.IsCurrent })
            .HasDatabaseName("ix_event_label_user_type_key_current");
        builder.HasIndex(x => new { x.UserId, x.EventId, x.SourceRevision, x.NormalizedValue })
            .HasDatabaseName("uk_event_label_event_revision_value");
        builder.HasOne(x => x.Event).WithMany(x => x.LabelIndexes)
            .HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class EventLocationConfiguration : IEntityTypeConfiguration<EventLocation>
{
    public void Configure(EntityTypeBuilder<EventLocation> builder)
    {
        builder.ToTable("event_location");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.EventId).HasColumnName("event_id");
        builder.Property(x => x.SourceRevisionId).HasColumnName("source_revision_id");
        builder.Property(x => x.SourceRevision).HasColumnName("source_revision");
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(256);
        builder.Property(x => x.Address).HasColumnName("address").HasMaxLength(512);
        builder.Property(x => x.Province).HasColumnName("province").HasMaxLength(128);
        builder.Property(x => x.City).HasColumnName("city").HasMaxLength(128);
        builder.Property(x => x.District).HasColumnName("district").HasMaxLength(128);
        builder.Property(x => x.AdCode).HasColumnName("ad_code").HasMaxLength(16);
        builder.Property(x => x.ProviderPoiId).HasColumnName("provider_poi_id").HasMaxLength(128);
        builder.Property(x => x.PoiType).HasColumnName("poi_type").HasMaxLength(128);
        builder.Property(x => x.Latitude).HasColumnName("latitude").HasPrecision(9, 6);
        builder.Property(x => x.Longitude).HasColumnName("longitude").HasPrecision(9, 6);
        builder.Property(x => x.AccuracyMeters).HasColumnName("accuracy_meters").HasPrecision(10, 2);
        builder.Property(x => x.CoordinateSystem).HasColumnName("coordinate_system").HasMaxLength(16);
        builder.Property(x => x.Source).HasColumnName("source").HasConversion<int>();
        builder.Property(x => x.CapturedAt).HasColumnName("captured_at");
        builder.Property(x => x.UserConfirmed).HasColumnName("user_confirmed");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.HasIndex(x => new { x.UserId, x.EventId, x.SourceRevision }).HasDatabaseName("ix_event_location_user_event_revision");
        builder.HasIndex(x => new { x.UserId, x.AdCode }).HasDatabaseName("ix_event_location_user_adcode");
        builder.HasOne(x => x.Revision).WithMany(x => x.Locations)
            .HasForeignKey(x => x.SourceRevisionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Event).WithMany(x => x.Locations)
            .HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class UserPlaceConfiguration : IEntityTypeConfiguration<UserPlace>
{
    public void Configure(EntityTypeBuilder<UserPlace> builder)
    {
        builder.ToTable("user_place");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.CanonicalKey).HasColumnName("canonical_key").HasMaxLength(512);
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(256);
        builder.Property(x => x.Address).HasColumnName("address").HasMaxLength(512);
        builder.Property(x => x.AdCode).HasColumnName("ad_code").HasMaxLength(16);
        builder.Property(x => x.ProviderPoiId).HasColumnName("provider_poi_id").HasMaxLength(128);
        builder.Property(x => x.Latitude).HasColumnName("latitude").HasPrecision(9, 6);
        builder.Property(x => x.Longitude).HasColumnName("longitude").HasPrecision(9, 6);
        builder.Property(x => x.CoordinateSystem).HasColumnName("coordinate_system").HasMaxLength(16);
        builder.Property(x => x.VisitCount).HasColumnName("visit_count");
        builder.Property(x => x.FirstVisitedAt).HasColumnName("first_visited_at");
        builder.Property(x => x.LastVisitedAt).HasColumnName("last_visited_at");
        builder.Property(x => x.RetrievalText).HasColumnName("retrieval_text");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property<Vector?>("Embedding").HasColumnName("embedding").HasColumnType("vector(1024)");
        builder.HasIndex(x => new { x.UserId, x.CanonicalKey }).IsUnique().HasDatabaseName("uk_user_place_user_key");
        builder.HasIndex("Embedding").HasMethod("hnsw").HasOperators("vector_cosine_ops").HasDatabaseName("ix_user_place_embedding");
    }
}
