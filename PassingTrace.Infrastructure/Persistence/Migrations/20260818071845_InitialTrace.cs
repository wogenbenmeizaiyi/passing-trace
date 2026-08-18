using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PassingTrace.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialTrace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "trace_event",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    event_kind = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    raw_content = table.Column<string>(type: "text", nullable: true),
                    happened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    planned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    timezone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    visibility = table.Column<int>(type: "integer", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    current_source_revision = table.Column<int>(type: "integer", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trace_event", x => x.id);
                },
                comment: "用户自由记录与计划的事实源，Trace 与 Plan 统一抽象。");

            migrationBuilder.CreateTable(
                name: "event_source_revision",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    event_id = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    raw_content = table.Column<string>(type: "text", nullable: true),
                    happened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    planned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_source_revision", x => x.id);
                    table.ForeignKey(
                        name: "FK_event_source_revision_trace_event_event_id",
                        column: x => x.event_id,
                        principalTable: "trace_event",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Event 的 Source 修订快照，旧值永不原地覆盖。");

            migrationBuilder.CreateIndex(
                name: "uk_event_source_revision_event_revision",
                table: "event_source_revision",
                columns: new[] { "event_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trace_event_user_created",
                table: "trace_event",
                columns: new[] { "user_id", "deleted_at", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_trace_event_user_id",
                table: "trace_event",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "uk_trace_event_user_idempotency",
                table: "trace_event",
                columns: new[] { "user_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "event_source_revision");

            migrationBuilder.DropTable(
                name: "trace_event");
        }
    }
}
