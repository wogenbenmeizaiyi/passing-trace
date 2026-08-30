using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;
using PassingTrace.Core.Ai;
using Pgvector;

namespace PassingTrace.Infrastructure.Persistence.Configurations;

public sealed class EventSemanticRunConfiguration : IEntityTypeConfiguration<EventSemanticRun>
{
    public void Configure(EntityTypeBuilder<EventSemanticRun> builder)
    {
        builder.ToTable("event_semantic_run");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.EventId).HasColumnName("event_id");
        builder.Property(x => x.SourceRevision).HasColumnName("source_revision");
        builder.Property(x => x.PipelineVersion).HasColumnName("pipeline_version").HasMaxLength(64);
        builder.Property(x => x.PromptVersion).HasColumnName("prompt_version").HasMaxLength(64);
        builder.Property(x => x.SchemaVersion).HasColumnName("schema_version").HasMaxLength(64);
        builder.Property(x => x.Model).HasColumnName("model").HasMaxLength(128);
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>();
        builder.Property(x => x.SemanticEnvelopeJson).HasColumnName("semantic_envelope").HasColumnType("jsonb");
        builder.Property(x => x.Summary).HasColumnName("summary");
        builder.Property(x => x.InputTokens).HasColumnName("input_tokens");
        builder.Property(x => x.OutputTokens).HasColumnName("output_tokens");
        builder.Property(x => x.DurationMilliseconds).HasColumnName("duration_ms");
        builder.Property(x => x.ErrorCode).HasColumnName("error_code").HasMaxLength(128);
        builder.Property(x => x.ErrorMessage).HasColumnName("error_message").HasMaxLength(2048);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.CompletedAt).HasColumnName("completed_at");
        builder.HasIndex(x => new { x.EventId, x.SourceRevision, x.PipelineVersion })
            .HasDatabaseName("ix_semantic_run_event_revision_pipeline");
        builder.HasIndex(x => new { x.UserId, x.Status, x.CreatedAt })
            .HasDatabaseName("ix_semantic_run_user_status_created");
        builder.HasOne(x => x.Event).WithMany(x => x.SemanticRuns)
            .HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class SemanticMentionConfiguration : IEntityTypeConfiguration<SemanticMention>
{
    public void Configure(EntityTypeBuilder<SemanticMention> builder)
    {
        builder.ToTable("semantic_mention");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(x => x.SemanticRunId).HasColumnName("semantic_run_id");
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.Category).HasColumnName("category").HasMaxLength(64);
        builder.Property(x => x.NormalizedValue).HasColumnName("normalized_value").HasMaxLength(512);
        builder.Property(x => x.OriginalValue).HasColumnName("original_value").HasMaxLength(512);
        builder.Property(x => x.Assertion).HasColumnName("assertion").HasConversion<int>();
        builder.Property(x => x.Confidence).HasColumnName("confidence").HasPrecision(5, 4);
        builder.Property(x => x.TextStart).HasColumnName("text_start");
        builder.Property(x => x.TextLength).HasColumnName("text_length");
        builder.Property(x => x.MediaAssetId).HasColumnName("media_asset_id");
        builder.HasIndex(x => new { x.UserId, x.Category, x.NormalizedValue })
            .HasDatabaseName("ix_semantic_mention_user_category_value");
        builder.HasOne(x => x.SemanticRun).WithMany(x => x.Mentions)
            .HasForeignKey(x => x.SemanticRunId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ExpenseFactConfiguration : IEntityTypeConfiguration<ExpenseFact>
{
    public void Configure(EntityTypeBuilder<ExpenseFact> builder)
    {
        builder.ToTable("expense_fact");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(x => x.SemanticRunId).HasColumnName("semantic_run_id");
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.Amount).HasColumnName("amount").HasPrecision(20, 4);
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(16);
        builder.Property(x => x.Purpose).HasColumnName("purpose").HasMaxLength(512);
        builder.Property(x => x.Scope).HasColumnName("scope").HasMaxLength(128);
        builder.Property(x => x.Confidence).HasColumnName("confidence").HasPrecision(5, 4);
        builder.Property(x => x.EvidenceJson).HasColumnName("evidence").HasColumnType("jsonb");
        builder.HasIndex(x => new { x.UserId, x.Currency }).HasDatabaseName("ix_expense_fact_user_currency");
        builder.HasOne(x => x.SemanticRun).WithMany(x => x.Expenses)
            .HasForeignKey(x => x.SemanticRunId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class EventSearchIndexConfiguration : IEntityTypeConfiguration<EventSearchIndex>
{
    public void Configure(EntityTypeBuilder<EventSearchIndex> builder)
    {
        builder.ToTable("event_search_index");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.EventId).HasColumnName("event_id");
        builder.Property(x => x.SourceRevision).HasColumnName("source_revision");
        builder.Property(x => x.SemanticRunId).HasColumnName("semantic_run_id");
        builder.Property(x => x.Title).HasColumnName("title");
        builder.Property(x => x.RawContent).HasColumnName("raw_content");
        builder.Property(x => x.AiSummary).HasColumnName("ai_summary");
        builder.Property(x => x.ImageDescriptions).HasColumnName("image_descriptions");
        builder.Property(x => x.RetrievalText).HasColumnName("retrieval_text");
        builder.Property(x => x.IsCurrent).HasColumnName("is_current");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property<NpgsqlTsVector>("SearchVector")
            .HasColumnName("search_vector")
            .HasComputedColumnSql("to_tsvector('simple', coalesce(retrieval_text, ''))", stored: true);
        builder.Property<Vector?>("Embedding").HasColumnName("embedding").HasColumnType("vector(1024)");
        builder.HasIndex(x => new { x.UserId, x.EventId, x.SourceRevision }).IsUnique()
            .HasDatabaseName("uk_event_search_index_user_event_revision");
        builder.HasIndex(x => new { x.UserId, x.IsCurrent }).HasDatabaseName("ix_event_search_index_user_current");
        builder.HasIndex("SearchVector").HasMethod("GIN").HasDatabaseName("ix_event_search_index_fts");
        builder.HasIndex(x => x.RetrievalText).HasMethod("GIN").HasOperators("gin_trgm_ops")
            .HasDatabaseName("ix_event_search_index_trgm");
        builder.HasIndex("Embedding").HasMethod("hnsw").HasOperators("vector_cosine_ops")
            .HasDatabaseName("ix_event_search_index_embedding");
    }
}
