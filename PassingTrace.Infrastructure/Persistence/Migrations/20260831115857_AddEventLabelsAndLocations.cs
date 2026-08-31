using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Pgvector;

#nullable disable

namespace PassingTrace.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEventLabelsAndLocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "event_label_index",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    event_id = table.Column<long>(type: "bigint", nullable: false),
                    source_revision = table.Column<int>(type: "integer", nullable: false),
                    semantic_run_id = table.Column<long>(type: "bigint", nullable: true),
                    label_type = table.Column<int>(type: "integer", nullable: false),
                    origin = table.Column<int>(type: "integer", nullable: false),
                    taxonomy_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    display_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    normalized_value = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    confidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    is_current = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_label_index", x => x.id);
                    table.ForeignKey(
                        name: "FK_event_label_index_trace_event_event_id",
                        column: x => x.event_id,
                        principalTable: "trace_event",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "event_location",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    event_id = table.Column<long>(type: "bigint", nullable: false),
                    source_revision_id = table.Column<long>(type: "bigint", nullable: false),
                    source_revision = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    address = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    province = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    city = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    district = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ad_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    provider_poi_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    poi_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    accuracy_meters = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    coordinate_system = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    source = table.Column<int>(type: "integer", nullable: false),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    user_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_location", x => x.id);
                    table.ForeignKey(
                        name: "FK_event_location_event_source_revision_source_revision_id",
                        column: x => x.source_revision_id,
                        principalTable: "event_source_revision",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_event_location_trace_event_event_id",
                        column: x => x.event_id,
                        principalTable: "trace_event",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "source_revision_label",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    source_revision_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    label_type = table.Column<int>(type: "integer", nullable: false),
                    decision = table.Column<int>(type: "integer", nullable: false),
                    taxonomy_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    display_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    normalized_value = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_source_revision_label", x => x.id);
                    table.ForeignKey(
                        name: "FK_source_revision_label_event_source_revision_source_revision~",
                        column: x => x.source_revision_id,
                        principalTable: "event_source_revision",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_place",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    canonical_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    address = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ad_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    provider_poi_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    coordinate_system = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    visit_count = table.Column<int>(type: "integer", nullable: false),
                    first_visited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_visited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    retrieval_text = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    embedding = table.Column<Vector>(type: "vector(1024)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_place", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_event_search_index_event_id",
                table: "event_search_index",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "IX_event_label_index_event_id",
                table: "event_label_index",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "ix_event_label_user_type_key_current",
                table: "event_label_index",
                columns: new[] { "user_id", "label_type", "taxonomy_key", "is_current" });

            migrationBuilder.CreateIndex(
                name: "uk_event_label_event_revision_value",
                table: "event_label_index",
                columns: new[] { "user_id", "event_id", "source_revision", "normalized_value" });

            migrationBuilder.CreateIndex(
                name: "IX_event_location_event_id",
                table: "event_location",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "IX_event_location_source_revision_id",
                table: "event_location",
                column: "source_revision_id");

            migrationBuilder.CreateIndex(
                name: "ix_event_location_user_adcode",
                table: "event_location",
                columns: new[] { "user_id", "ad_code" });

            migrationBuilder.CreateIndex(
                name: "ix_event_location_user_event_revision",
                table: "event_location",
                columns: new[] { "user_id", "event_id", "source_revision" });

            migrationBuilder.CreateIndex(
                name: "ix_source_label_user_value",
                table: "source_revision_label",
                columns: new[] { "user_id", "normalized_value" });

            migrationBuilder.CreateIndex(
                name: "IX_source_revision_label_source_revision_id",
                table: "source_revision_label",
                column: "source_revision_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_place_embedding",
                table: "user_place",
                column: "embedding")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });

            migrationBuilder.CreateIndex(
                name: "uk_user_place_user_key",
                table: "user_place",
                columns: new[] { "user_id", "canonical_key" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_event_search_index_trace_event_event_id",
                table: "event_search_index",
                column: "event_id",
                principalTable: "trace_event",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            // 新 Schema/Prompt 上线后限速补算当前记录。低优先级保证新记录先处理。
            migrationBuilder.Sql("""
                INSERT INTO outbox_message
                    (id, user_id, message_type, event_id, source_revision, priority, payload, status,
                     attempts, available_at, created_at)
                SELECT md5(e.id::text || clock_timestamp()::text || random()::text)::uuid,
                       e.user_id, 'event.analyze', e.id, e.current_source_revision, 10,
                       '{"force":true}'::jsonb, 1, 0, now(), now()
                FROM trace_event e
                WHERE e.deleted_at IS NULL
                  AND NOT EXISTS (
                    SELECT 1 FROM outbox_message o
                    WHERE o.event_id = e.id AND o.source_revision = e.current_source_revision
                      AND o.message_type = 'event.analyze' AND o.status IN (1, 2));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_event_search_index_trace_event_event_id",
                table: "event_search_index");

            migrationBuilder.DropTable(
                name: "event_label_index");

            migrationBuilder.DropTable(
                name: "event_location");

            migrationBuilder.DropTable(
                name: "source_revision_label");

            migrationBuilder.DropTable(
                name: "user_place");

            migrationBuilder.DropIndex(
                name: "IX_event_search_index_event_id",
                table: "event_search_index");
        }
    }
}
