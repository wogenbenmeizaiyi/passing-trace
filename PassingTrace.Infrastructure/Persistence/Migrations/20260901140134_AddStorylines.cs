using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using NpgsqlTypes;
using Pgvector;

#nullable disable

namespace PassingTrace.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStorylines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "storyline",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    category_key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    current_revision = table.Column<int>(type: "integer", nullable: false),
                    creation_idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    cover_media_asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    range_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    range_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storyline", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "storyline_revision",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    storyline_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    category_key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    cover_media_asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    range_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    range_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    layout_state = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storyline_revision", x => x.id);
                    table.ForeignKey(
                        name: "FK_storyline_revision_storyline_storyline_id",
                        column: x => x.storyline_id,
                        principalTable: "storyline",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "storyline_search_index",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    storyline_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    retrieval_text = table.Column<string>(type: "text", nullable: false),
                    is_current = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    embedding = table.Column<Vector>(type: "vector(1024)", nullable: true),
                    search_vector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "to_tsvector('simple', coalesce(retrieval_text, ''))", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storyline_search_index", x => x.id);
                    table.ForeignKey(
                        name: "FK_storyline_search_index_storyline_storyline_id",
                        column: x => x.storyline_id,
                        principalTable: "storyline",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "storyline_edge",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    storyline_revision_id = table.Column<long>(type: "bigint", nullable: false),
                    edge_key = table.Column<Guid>(type: "uuid", nullable: false),
                    source_node_key = table.Column<Guid>(type: "uuid", nullable: false),
                    target_node_key = table.Column<Guid>(type: "uuid", nullable: false),
                    relation_type = table.Column<int>(type: "integer", nullable: false),
                    label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storyline_edge", x => x.id);
                    table.ForeignKey(
                        name: "FK_storyline_edge_storyline_revision_storyline_revision_id",
                        column: x => x.storyline_revision_id,
                        principalTable: "storyline_revision",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "storyline_node",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    storyline_revision_id = table.Column<long>(type: "bigint", nullable: false),
                    node_key = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<long>(type: "bigint", nullable: false),
                    source_revision = table.Column<int>(type: "integer", nullable: false),
                    stage_key = table.Column<Guid>(type: "uuid", nullable: true),
                    semantic_order = table.Column<int>(type: "integer", nullable: false),
                    emphasis = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storyline_node", x => x.id);
                    table.ForeignKey(
                        name: "FK_storyline_node_storyline_revision_storyline_revision_id",
                        column: x => x.storyline_revision_id,
                        principalTable: "storyline_revision",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_storyline_node_trace_event_event_id",
                        column: x => x.event_id,
                        principalTable: "trace_event",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "storyline_revision_tag",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    storyline_revision_id = table.Column<long>(type: "bigint", nullable: false),
                    origin = table.Column<int>(type: "integer", nullable: false),
                    taxonomy_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    display_name = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    normalized_value = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storyline_revision_tag", x => x.id);
                    table.ForeignKey(
                        name: "FK_storyline_revision_tag_storyline_revision_storyline_revisio~",
                        column: x => x.storyline_revision_id,
                        principalTable: "storyline_revision",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "storyline_stage",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    storyline_revision_id = table.Column<long>(type: "bigint", nullable: false),
                    stage_key = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    semantic_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storyline_stage", x => x.id);
                    table.ForeignKey(
                        name: "FK_storyline_stage_storyline_revision_storyline_revision_id",
                        column: x => x.storyline_revision_id,
                        principalTable: "storyline_revision",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "storyline_web_layout",
                columns: table => new
                {
                    storyline_revision_id = table.Column<long>(type: "bigint", nullable: false),
                    direction = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    viewport_x = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    viewport_y = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    zoom = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storyline_web_layout", x => x.storyline_revision_id);
                    table.ForeignKey(
                        name: "FK_storyline_web_layout_storyline_revision_storyline_revision_~",
                        column: x => x.storyline_revision_id,
                        principalTable: "storyline_revision",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "storyline_web_node_layout",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    storyline_revision_id = table.Column<long>(type: "bigint", nullable: false),
                    node_key = table.Column<Guid>(type: "uuid", nullable: false),
                    x = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    y = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    width = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true),
                    height = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storyline_web_node_layout", x => x.id);
                    table.ForeignKey(
                        name: "FK_storyline_web_node_layout_storyline_web_layout_storyline_re~",
                        column: x => x.storyline_revision_id,
                        principalTable: "storyline_web_layout",
                        principalColumn: "storyline_revision_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "storyline_web_stage_layout",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    storyline_revision_id = table.Column<long>(type: "bigint", nullable: false),
                    stage_key = table.Column<Guid>(type: "uuid", nullable: false),
                    x = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    y = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    width = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    height = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storyline_web_stage_layout", x => x.id);
                    table.ForeignKey(
                        name: "FK_storyline_web_stage_layout_storyline_web_layout_storyline_r~",
                        column: x => x.storyline_revision_id,
                        principalTable: "storyline_web_layout",
                        principalColumn: "storyline_revision_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_storyline_user_category_status",
                table: "storyline",
                columns: new[] { "user_id", "category_key", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_storyline_user_updated",
                table: "storyline",
                columns: new[] { "user_id", "deleted_at", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "uk_storyline_user_creation_idempotency",
                table: "storyline",
                columns: new[] { "user_id", "creation_idempotency_key" },
                unique: true,
                filter: "creation_idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "uk_storyline_edge_key",
                table: "storyline_edge",
                columns: new[] { "storyline_revision_id", "edge_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uk_storyline_edge_relation",
                table: "storyline_edge",
                columns: new[] { "storyline_revision_id", "source_node_key", "target_node_key", "relation_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_storyline_node_event_id",
                table: "storyline_node",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "uk_storyline_node_event",
                table: "storyline_node",
                columns: new[] { "storyline_revision_id", "event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uk_storyline_node_key",
                table: "storyline_node",
                columns: new[] { "storyline_revision_id", "node_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uk_storyline_revision_idempotency",
                table: "storyline_revision",
                columns: new[] { "storyline_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "uk_storyline_revision_number",
                table: "storyline_revision",
                columns: new[] { "storyline_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uk_storyline_revision_tag",
                table: "storyline_revision_tag",
                columns: new[] { "storyline_revision_id", "normalized_value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_storyline_search_current",
                table: "storyline_search_index",
                columns: new[] { "user_id", "is_current" });

            migrationBuilder.CreateIndex(
                name: "ix_storyline_search_embedding",
                table: "storyline_search_index",
                column: "embedding")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_storyline_search_fts",
                table: "storyline_search_index",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "IX_storyline_search_index_storyline_id",
                table: "storyline_search_index",
                column: "storyline_id");

            migrationBuilder.CreateIndex(
                name: "ix_storyline_search_trgm",
                table: "storyline_search_index",
                column: "retrieval_text")
                .Annotation("Npgsql:IndexMethod", "GIN")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "uk_storyline_search_revision",
                table: "storyline_search_index",
                columns: new[] { "user_id", "storyline_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uk_storyline_stage_key",
                table: "storyline_stage",
                columns: new[] { "storyline_revision_id", "stage_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uk_storyline_web_node_layout",
                table: "storyline_web_node_layout",
                columns: new[] { "storyline_revision_id", "node_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uk_storyline_web_stage_layout",
                table: "storyline_web_stage_layout",
                columns: new[] { "storyline_revision_id", "stage_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "storyline_edge");

            migrationBuilder.DropTable(
                name: "storyline_node");

            migrationBuilder.DropTable(
                name: "storyline_revision_tag");

            migrationBuilder.DropTable(
                name: "storyline_search_index");

            migrationBuilder.DropTable(
                name: "storyline_stage");

            migrationBuilder.DropTable(
                name: "storyline_web_node_layout");

            migrationBuilder.DropTable(
                name: "storyline_web_stage_layout");

            migrationBuilder.DropTable(
                name: "storyline_web_layout");

            migrationBuilder.DropTable(
                name: "storyline_revision");

            migrationBuilder.DropTable(
                name: "storyline");
        }
    }
}
