using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using NpgsqlTypes;
using Pgvector;

#nullable disable

namespace PassingTrace.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaAiMemory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "ai_conversation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_conversation", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "event_search_index",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    event_id = table.Column<long>(type: "bigint", nullable: false),
                    source_revision = table.Column<int>(type: "integer", nullable: false),
                    semantic_run_id = table.Column<long>(type: "bigint", nullable: true),
                    title = table.Column<string>(type: "text", nullable: false),
                    raw_content = table.Column<string>(type: "text", nullable: false),
                    ai_summary = table.Column<string>(type: "text", nullable: false),
                    image_descriptions = table.Column<string>(type: "text", nullable: false),
                    retrieval_text = table.Column<string>(type: "text", nullable: false),
                    is_current = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    embedding = table.Column<Vector>(type: "vector(1024)", nullable: true),
                    search_vector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "to_tsvector('simple', coalesce(retrieval_text, ''))", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_search_index", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "event_semantic_run",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    event_id = table.Column<long>(type: "bigint", nullable: false),
                    source_revision = table.Column<int>(type: "integer", nullable: false),
                    pipeline_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    prompt_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    schema_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    semantic_envelope = table.Column<string>(type: "jsonb", nullable: true),
                    summary = table.Column<string>(type: "text", nullable: true),
                    input_tokens = table.Column<int>(type: "integer", nullable: true),
                    output_tokens = table.Column<int>(type: "integer", nullable: true),
                    duration_ms = table.Column<long>(type: "bigint", nullable: true),
                    error_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    error_message = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_semantic_run", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "media_asset",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    object_key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    original_file_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    declared_mime_type = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    verified_mime_type = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    expected_size = table.Column<long>(type: "bigint", nullable: false),
                    actual_size = table.Column<long>(type: "bigint", nullable: true),
                    expected_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    actual_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    upload_mode = table.Column<int>(type: "integer", nullable: false),
                    multipart_upload_id = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    upload_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    processing_error = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_asset", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_message",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    message_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    event_id = table.Column<long>(type: "bigint", nullable: true),
                    source_revision = table.Column<int>(type: "integer", nullable: true),
                    media_asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    available_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    lease_owner = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    lease_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_message", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_data_watermark",
                columns: table => new
                {
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_data_watermark", x => x.user_id);
                });

            migrationBuilder.CreateTable(
                name: "user_memory",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    memory_type = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    confidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    rejected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    embedding = table.Column<Vector>(type: "vector(1024)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_memory", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ai_message",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    role = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    evidence_snapshot = table.Column<string>(type: "jsonb", nullable: true),
                    model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    prompt_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    data_watermark = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_message", x => x.id);
                    table.ForeignKey(
                        name: "FK_ai_message_ai_conversation_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "ai_conversation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "conversation_summary",
                columns: table => new
                {
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    through_message_id = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversation_summary", x => x.conversation_id);
                    table.ForeignKey(
                        name: "FK_conversation_summary_ai_conversation_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "ai_conversation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "expense_fact",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    semantic_run_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    purpose = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    scope = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    confidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    evidence = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expense_fact", x => x.id);
                    table.ForeignKey(
                        name: "FK_expense_fact_event_semantic_run_semantic_run_id",
                        column: x => x.semantic_run_id,
                        principalTable: "event_semantic_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "semantic_mention",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    semantic_run_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    normalized_value = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    original_value = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    assertion = table.Column<int>(type: "integer", nullable: false),
                    confidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    text_start = table.Column<int>(type: "integer", nullable: true),
                    text_length = table.Column<int>(type: "integer", nullable: true),
                    media_asset_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_semantic_mention", x => x.id);
                    table.ForeignKey(
                        name: "FK_semantic_mention_event_semantic_run_semantic_run_id",
                        column: x => x.semantic_run_id,
                        principalTable: "event_semantic_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "event_media_asset",
                columns: table => new
                {
                    event_id = table.Column<long>(type: "bigint", nullable: false),
                    media_asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_media_asset", x => new { x.event_id, x.media_asset_id });
                    table.ForeignKey(
                        name: "FK_event_media_asset_media_asset_media_asset_id",
                        column: x => x.media_asset_id,
                        principalTable: "media_asset",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_event_media_asset_trace_event_event_id",
                        column: x => x.event_id,
                        principalTable: "trace_event",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "event_source_revision_media",
                columns: table => new
                {
                    source_revision_id = table.Column<long>(type: "bigint", nullable: false),
                    media_asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_source_revision_media", x => new { x.source_revision_id, x.media_asset_id });
                    table.ForeignKey(
                        name: "FK_event_source_revision_media_event_source_revision_source_re~",
                        column: x => x.source_revision_id,
                        principalTable: "event_source_revision",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_event_source_revision_media_media_asset_media_asset_id",
                        column: x => x.media_asset_id,
                        principalTable: "media_asset",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_memory_evidence",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_memory_id = table.Column<long>(type: "bigint", nullable: false),
                    event_id = table.Column<long>(type: "bigint", nullable: false),
                    source_revision = table.Column<int>(type: "integer", nullable: false),
                    semantic_run_id = table.Column<long>(type: "bigint", nullable: false),
                    evidence = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_memory_evidence", x => x.id);
                    table.ForeignKey(
                        name: "FK_user_memory_evidence_user_memory_user_memory_id",
                        column: x => x.user_memory_id,
                        principalTable: "user_memory",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ai_conversation_user_updated",
                table: "ai_conversation",
                columns: new[] { "user_id", "deleted_at", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_ai_message_conversation_id",
                table: "ai_message",
                column: "conversation_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_message_user_conversation",
                table: "ai_message",
                columns: new[] { "user_id", "conversation_id", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_event_media_asset_media_asset_id",
                table: "event_media_asset",
                column: "media_asset_id");

            migrationBuilder.CreateIndex(
                name: "uk_event_media_asset_order",
                table: "event_media_asset",
                columns: new[] { "event_id", "sort_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_event_search_index_embedding",
                table: "event_search_index",
                column: "embedding")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_event_search_index_fts",
                table: "event_search_index",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "ix_event_search_index_user_current",
                table: "event_search_index",
                columns: new[] { "user_id", "is_current" });

            migrationBuilder.CreateIndex(
                name: "uk_event_search_index_user_event_revision",
                table: "event_search_index",
                columns: new[] { "user_id", "event_id", "source_revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_semantic_run_event_revision_pipeline",
                table: "event_semantic_run",
                columns: new[] { "event_id", "source_revision", "pipeline_version" });

            migrationBuilder.CreateIndex(
                name: "ix_semantic_run_user_status_created",
                table: "event_semantic_run",
                columns: new[] { "user_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_event_source_revision_media_media_asset_id",
                table: "event_source_revision_media",
                column: "media_asset_id");

            migrationBuilder.CreateIndex(
                name: "uk_event_source_revision_media_order",
                table: "event_source_revision_media",
                columns: new[] { "source_revision_id", "sort_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_expense_fact_semantic_run_id",
                table: "expense_fact",
                column: "semantic_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_expense_fact_user_currency",
                table: "expense_fact",
                columns: new[] { "user_id", "currency" });

            migrationBuilder.CreateIndex(
                name: "ix_media_asset_user_status_created",
                table: "media_asset",
                columns: new[] { "user_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "uk_media_asset_object_key",
                table: "media_asset",
                column: "object_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_claim",
                table: "outbox_message",
                columns: new[] { "status", "available_at", "priority" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_event_revision_type",
                table: "outbox_message",
                columns: new[] { "event_id", "source_revision", "message_type" });

            migrationBuilder.CreateIndex(
                name: "IX_semantic_mention_semantic_run_id",
                table: "semantic_mention",
                column: "semantic_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_semantic_mention_user_category_value",
                table: "semantic_mention",
                columns: new[] { "user_id", "category", "normalized_value" });

            migrationBuilder.CreateIndex(
                name: "ix_user_memory_embedding",
                table: "user_memory",
                column: "embedding")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_user_memory_user_status_updated",
                table: "user_memory",
                columns: new[] { "user_id", "status", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "uk_user_memory_user_fingerprint",
                table: "user_memory",
                columns: new[] { "user_id", "fingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uk_user_memory_evidence_source",
                table: "user_memory_evidence",
                columns: new[] { "user_memory_id", "event_id", "source_revision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_message");

            migrationBuilder.DropTable(
                name: "conversation_summary");

            migrationBuilder.DropTable(
                name: "event_media_asset");

            migrationBuilder.DropTable(
                name: "event_search_index");

            migrationBuilder.DropTable(
                name: "event_source_revision_media");

            migrationBuilder.DropTable(
                name: "expense_fact");

            migrationBuilder.DropTable(
                name: "outbox_message");

            migrationBuilder.DropTable(
                name: "semantic_mention");

            migrationBuilder.DropTable(
                name: "user_data_watermark");

            migrationBuilder.DropTable(
                name: "user_memory_evidence");

            migrationBuilder.DropTable(
                name: "ai_conversation");

            migrationBuilder.DropTable(
                name: "media_asset");

            migrationBuilder.DropTable(
                name: "event_semantic_run");

            migrationBuilder.DropTable(
                name: "user_memory");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:vector", ",,");
        }
    }
}
