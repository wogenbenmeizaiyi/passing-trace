using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;
using PassingTrace.Core.Storylines;
using Pgvector;

namespace PassingTrace.Infrastructure.Persistence.Configurations;

public sealed class StorylineConfiguration : IEntityTypeConfiguration<Storyline>
{
    public void Configure(EntityTypeBuilder<Storyline> builder)
    {
        builder.ToTable("storyline");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(120);
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(2000);
        builder.Property(x => x.CategoryKey).HasColumnName("category_key").HasMaxLength(32);
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>();
        builder.Property(x => x.CurrentRevision).HasColumnName("current_revision");
        builder.Property(x => x.CreationIdempotencyKey).HasColumnName("creation_idempotency_key").HasMaxLength(128);
        builder.Property(x => x.CoverMediaAssetId).HasColumnName("cover_media_asset_id");
        builder.Property(x => x.RangeStart).HasColumnName("range_start");
        builder.Property(x => x.RangeEnd).HasColumnName("range_end");
        builder.Property(x => x.ArchivedAt).HasColumnName("archived_at");
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.UserId, x.DeletedAt, x.UpdatedAt }).HasDatabaseName("ix_storyline_user_updated");
        builder.HasIndex(x => new { x.UserId, x.CreationIdempotencyKey }).IsUnique()
            .HasFilter("creation_idempotency_key IS NOT NULL").HasDatabaseName("uk_storyline_user_creation_idempotency");
        builder.HasIndex(x => new { x.UserId, x.CategoryKey, x.Status }).HasDatabaseName("ix_storyline_user_category_status");
        builder.HasMany(x => x.Revisions).WithOne(x => x.Storyline).HasForeignKey(x => x.StorylineId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class StorylineRevisionConfiguration : IEntityTypeConfiguration<StorylineRevision>
{
    public void Configure(EntityTypeBuilder<StorylineRevision> builder)
    {
        builder.ToTable("storyline_revision");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(x => x.StorylineId).HasColumnName("storyline_id");
        builder.Property(x => x.Revision).HasColumnName("revision");
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(128);
        builder.Property(x => x.ContentHash).HasColumnName("content_hash").HasMaxLength(64);
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(120);
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(2000);
        builder.Property(x => x.CategoryKey).HasColumnName("category_key").HasMaxLength(32);
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>();
        builder.Property(x => x.CoverMediaAssetId).HasColumnName("cover_media_asset_id");
        builder.Property(x => x.RangeStart).HasColumnName("range_start");
        builder.Property(x => x.RangeEnd).HasColumnName("range_end");
        builder.Property(x => x.LayoutState).HasColumnName("layout_state").HasConversion<int>();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.HasIndex(x => new { x.StorylineId, x.Revision }).IsUnique().HasDatabaseName("uk_storyline_revision_number");
        builder.HasIndex(x => new { x.StorylineId, x.IdempotencyKey }).IsUnique()
            .HasFilter("idempotency_key IS NOT NULL").HasDatabaseName("uk_storyline_revision_idempotency");
        builder.HasMany(x => x.Stages).WithOne(x => x.Revision).HasForeignKey(x => x.StorylineRevisionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Nodes).WithOne(x => x.Revision).HasForeignKey(x => x.StorylineRevisionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Edges).WithOne(x => x.Revision).HasForeignKey(x => x.StorylineRevisionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Tags).WithOne(x => x.Revision).HasForeignKey(x => x.StorylineRevisionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class StorylineStageConfiguration : IEntityTypeConfiguration<StorylineStage>
{
    public void Configure(EntityTypeBuilder<StorylineStage> builder)
    {
        builder.ToTable("storyline_stage"); builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(x => x.StorylineRevisionId).HasColumnName("storyline_revision_id");
        builder.Property(x => x.Key).HasColumnName("stage_key");
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(120);
        builder.Property(x => x.SemanticOrder).HasColumnName("semantic_order");
        builder.HasIndex(x => new { x.StorylineRevisionId, x.Key }).IsUnique().HasDatabaseName("uk_storyline_stage_key");
    }
}

public sealed class StorylineNodeConfiguration : IEntityTypeConfiguration<StorylineNode>
{
    public void Configure(EntityTypeBuilder<StorylineNode> builder)
    {
        builder.ToTable("storyline_node"); builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(x => x.StorylineRevisionId).HasColumnName("storyline_revision_id");
        builder.Property(x => x.Key).HasColumnName("node_key");
        builder.Property(x => x.EventId).HasColumnName("event_id");
        builder.Property(x => x.SourceRevision).HasColumnName("source_revision");
        builder.Property(x => x.StageKey).HasColumnName("stage_key");
        builder.Property(x => x.SemanticOrder).HasColumnName("semantic_order");
        builder.Property(x => x.Emphasis).HasColumnName("emphasis").HasConversion<int>();
        builder.HasIndex(x => new { x.StorylineRevisionId, x.Key }).IsUnique().HasDatabaseName("uk_storyline_node_key");
        builder.HasIndex(x => new { x.StorylineRevisionId, x.EventId }).IsUnique().HasDatabaseName("uk_storyline_node_event");
        builder.HasOne(x => x.Event).WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class StorylineEdgeConfiguration : IEntityTypeConfiguration<StorylineEdge>
{
    public void Configure(EntityTypeBuilder<StorylineEdge> builder)
    {
        builder.ToTable("storyline_edge"); builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(x => x.StorylineRevisionId).HasColumnName("storyline_revision_id");
        builder.Property(x => x.Key).HasColumnName("edge_key");
        builder.Property(x => x.SourceNodeKey).HasColumnName("source_node_key");
        builder.Property(x => x.TargetNodeKey).HasColumnName("target_node_key");
        builder.Property(x => x.RelationType).HasColumnName("relation_type").HasConversion<int>();
        builder.Property(x => x.Label).HasColumnName("label").HasMaxLength(120);
        builder.HasIndex(x => new { x.StorylineRevisionId, x.Key }).IsUnique().HasDatabaseName("uk_storyline_edge_key");
        builder.HasIndex(x => new { x.StorylineRevisionId, x.SourceNodeKey, x.TargetNodeKey, x.RelationType }).IsUnique()
            .HasDatabaseName("uk_storyline_edge_relation");
    }
}

public sealed class StorylineRevisionTagConfiguration : IEntityTypeConfiguration<StorylineRevisionTag>
{
    public void Configure(EntityTypeBuilder<StorylineRevisionTag> builder)
    {
        builder.ToTable("storyline_revision_tag"); builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(x => x.StorylineRevisionId).HasColumnName("storyline_revision_id");
        builder.Property(x => x.Origin).HasColumnName("origin").HasConversion<int>();
        builder.Property(x => x.TaxonomyKey).HasColumnName("taxonomy_key").HasMaxLength(64);
        builder.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(24);
        builder.Property(x => x.NormalizedValue).HasColumnName("normalized_value").HasMaxLength(64);
        builder.Property(x => x.SortOrder).HasColumnName("sort_order");
        builder.HasIndex(x => new { x.StorylineRevisionId, x.NormalizedValue }).IsUnique().HasDatabaseName("uk_storyline_revision_tag");
    }
}

public sealed class StorylineWebLayoutConfiguration : IEntityTypeConfiguration<StorylineWebLayout>
{
    public void Configure(EntityTypeBuilder<StorylineWebLayout> builder)
    {
        builder.ToTable("storyline_web_layout"); builder.HasKey(x => x.StorylineRevisionId);
        builder.Property(x => x.StorylineRevisionId).HasColumnName("storyline_revision_id").ValueGeneratedNever();
        builder.Property(x => x.Direction).HasColumnName("direction").HasMaxLength(4);
        builder.Property(x => x.ViewportX).HasColumnName("viewport_x").HasPrecision(12, 3);
        builder.Property(x => x.ViewportY).HasColumnName("viewport_y").HasPrecision(12, 3);
        builder.Property(x => x.Zoom).HasColumnName("zoom").HasPrecision(8, 4);
        builder.HasOne(x => x.Revision).WithOne(x => x.WebLayout).HasForeignKey<StorylineWebLayout>(x => x.StorylineRevisionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Nodes).WithOne(x => x.Layout).HasForeignKey(x => x.StorylineRevisionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Stages).WithOne(x => x.Layout).HasForeignKey(x => x.StorylineRevisionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class StorylineWebNodeLayoutConfiguration : IEntityTypeConfiguration<StorylineWebNodeLayout>
{
    public void Configure(EntityTypeBuilder<StorylineWebNodeLayout> builder)
    {
        builder.ToTable("storyline_web_node_layout"); builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(x => x.StorylineRevisionId).HasColumnName("storyline_revision_id");
        builder.Property(x => x.NodeKey).HasColumnName("node_key");
        builder.Property(x => x.X).HasColumnName("x").HasPrecision(12, 3);
        builder.Property(x => x.Y).HasColumnName("y").HasPrecision(12, 3);
        builder.Property(x => x.Width).HasColumnName("width").HasPrecision(12, 3);
        builder.Property(x => x.Height).HasColumnName("height").HasPrecision(12, 3);
        builder.HasIndex(x => new { x.StorylineRevisionId, x.NodeKey }).IsUnique().HasDatabaseName("uk_storyline_web_node_layout");
    }
}

public sealed class StorylineWebStageLayoutConfiguration : IEntityTypeConfiguration<StorylineWebStageLayout>
{
    public void Configure(EntityTypeBuilder<StorylineWebStageLayout> builder)
    {
        builder.ToTable("storyline_web_stage_layout"); builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(x => x.StorylineRevisionId).HasColumnName("storyline_revision_id");
        builder.Property(x => x.StageKey).HasColumnName("stage_key");
        builder.Property(x => x.X).HasColumnName("x").HasPrecision(12, 3);
        builder.Property(x => x.Y).HasColumnName("y").HasPrecision(12, 3);
        builder.Property(x => x.Width).HasColumnName("width").HasPrecision(12, 3);
        builder.Property(x => x.Height).HasColumnName("height").HasPrecision(12, 3);
        builder.HasIndex(x => new { x.StorylineRevisionId, x.StageKey }).IsUnique().HasDatabaseName("uk_storyline_web_stage_layout");
    }
}

public sealed class StorylineSearchIndexConfiguration : IEntityTypeConfiguration<StorylineSearchIndex>
{
    public void Configure(EntityTypeBuilder<StorylineSearchIndex> builder)
    {
        builder.ToTable("storyline_search_index"); builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.StorylineId).HasColumnName("storyline_id");
        builder.Property(x => x.Revision).HasColumnName("revision");
        builder.Property(x => x.RetrievalText).HasColumnName("retrieval_text");
        builder.Property(x => x.IsCurrent).HasColumnName("is_current");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property<NpgsqlTsVector>("SearchVector").HasColumnName("search_vector")
            .HasComputedColumnSql("to_tsvector('simple', coalesce(retrieval_text, ''))", stored: true);
        builder.Property<Vector?>("Embedding").HasColumnName("embedding").HasColumnType("vector(1024)");
        builder.HasIndex(x => new { x.UserId, x.StorylineId, x.Revision }).IsUnique().HasDatabaseName("uk_storyline_search_revision");
        builder.HasIndex(x => new { x.UserId, x.IsCurrent }).HasDatabaseName("ix_storyline_search_current");
        builder.HasIndex("SearchVector").HasMethod("GIN").HasDatabaseName("ix_storyline_search_fts");
        builder.HasIndex(x => x.RetrievalText).HasMethod("GIN").HasOperators("gin_trgm_ops").HasDatabaseName("ix_storyline_search_trgm");
        builder.HasIndex("Embedding").HasMethod("hnsw").HasOperators("vector_cosine_ops").HasDatabaseName("ix_storyline_search_embedding");
        builder.HasOne(x => x.Storyline).WithMany(x => x.SearchIndexes).HasForeignKey(x => x.StorylineId).OnDelete(DeleteBehavior.Cascade);
    }
}
