using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PassingTrace.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SubjectIdsJson",
                table: "trace_event",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "SubjectIdsJson",
                table: "event_source_revision",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.CreateTable(
                name: "subject",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    IsSelf = table.Column<bool>(type: "boolean", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    ItemType = table.Column<string>(type: "text", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Timezone = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndReason = table.Column<string>(type: "text", nullable: true),
                    FieldsJson = table.Column<string>(type: "text", nullable: false),
                    ValuesJson = table.Column<string>(type: "text", nullable: false),
                    MediaIdsJson = table.Column<string>(type: "text", nullable: false),
                    CoverMediaId = table.Column<Guid>(type: "uuid", nullable: true),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: true),
                    RequestHash = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subject", x => x.Id);
                    table.CheckConstraint("ck_subject_self", "NOT \"IsSelf\" OR (\"DeletedAt\" IS NULL AND \"State\" = 0 AND \"Kind\" = 0)");
                });

            migrationBuilder.CreateTable(
                name: "subject_history",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    TargetType = table.Column<string>(type: "text", nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    SnapshotJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subject_history", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "subject_media_reference",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    TargetType = table.Column<string>(type: "text", nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subject_media_reference", x => x.Id);
                    table.ForeignKey(
                        name: "FK_subject_media_reference_media_asset_MediaId",
                        column: x => x.MediaId,
                        principalTable: "media_asset",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "subject_entry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: true),
                    HappenedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PlannedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Timezone = table.Column<string>(type: "text", nullable: false),
                    FieldChangesJson = table.Column<string>(type: "text", nullable: false),
                    ActualFieldChangesJson = table.Column<string>(type: "text", nullable: false),
                    FieldDefinitionsJson = table.Column<string>(type: "text", nullable: false),
                    MediaIdsJson = table.Column<string>(type: "text", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: true),
                    RequestHash = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subject_entry", x => x.Id);
                    table.ForeignKey(
                        name: "FK_subject_entry_subject_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "subject",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "subject_milestone",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Operation = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    Note = table.Column<string>(type: "text", nullable: true),
                    EffectiveAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VoidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subject_milestone", x => x.Id);
                    table.ForeignKey(
                        name: "FK_subject_milestone_subject_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "subject",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "subject_relation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    FromSubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToSubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Directed = table.Column<bool>(type: "boolean", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RemovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subject_relation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_subject_relation_subject_FromSubjectId",
                        column: x => x.FromSubjectId,
                        principalTable: "subject",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_subject_relation_subject_ToSubjectId",
                        column: x => x.ToSubjectId,
                        principalTable: "subject",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "subject_timeline_reference",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<long>(type: "bigint", nullable: true),
                    EntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RemovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subject_timeline_reference", x => x.Id);
                    table.CheckConstraint("ck_subject_reference_source", "(\"EventId\" IS NULL) <> (\"EntryId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_subject_timeline_reference_subject_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "subject",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_subject_timeline_reference_subject_entry_EntryId",
                        column: x => x.EntryId,
                        principalTable: "subject_entry",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_subject_timeline_reference_trace_event_EventId",
                        column: x => x.EventId,
                        principalTable: "trace_event",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_subject_UserId",
                table: "subject",
                column: "UserId",
                unique: true,
                filter: "\"IsSelf\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_subject_UserId_DeletedAt",
                table: "subject",
                columns: new[] { "UserId", "DeletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_subject_UserId_IdempotencyKey",
                table: "subject",
                columns: new[] { "UserId", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_subject_entry_SubjectId",
                table: "subject_entry",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_subject_entry_UserId_IdempotencyKey",
                table: "subject_entry",
                columns: new[] { "UserId", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_subject_entry_UserId_SubjectId_CreatedAt",
                table: "subject_entry",
                columns: new[] { "UserId", "SubjectId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_subject_history_UserId_TargetType_TargetId_Revision",
                table: "subject_history",
                columns: new[] { "UserId", "TargetType", "TargetId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_subject_media_reference_MediaId",
                table: "subject_media_reference",
                column: "MediaId");

            migrationBuilder.CreateIndex(
                name: "IX_subject_media_reference_UserId_TargetType_TargetId_MediaId_~",
                table: "subject_media_reference",
                columns: new[] { "UserId", "TargetType", "TargetId", "MediaId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_subject_milestone_SubjectId",
                table: "subject_milestone",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_subject_milestone_UserId_SubjectId_EffectiveAt",
                table: "subject_milestone",
                columns: new[] { "UserId", "SubjectId", "EffectiveAt" });

            migrationBuilder.CreateIndex(
                name: "IX_subject_relation_FromSubjectId",
                table: "subject_relation",
                column: "FromSubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_subject_relation_ToSubjectId",
                table: "subject_relation",
                column: "ToSubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_subject_relation_UserId",
                table: "subject_relation",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_subject_timeline_reference_EntryId",
                table: "subject_timeline_reference",
                column: "EntryId");

            migrationBuilder.CreateIndex(
                name: "IX_subject_timeline_reference_EventId",
                table: "subject_timeline_reference",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_subject_timeline_reference_SubjectId",
                table: "subject_timeline_reference",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_subject_timeline_reference_UserId_SubjectId_EntryId",
                table: "subject_timeline_reference",
                columns: new[] { "UserId", "SubjectId", "EntryId" },
                unique: true,
                filter: "\"EntryId\" IS NOT NULL AND \"RemovedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_subject_timeline_reference_UserId_SubjectId_EventId",
                table: "subject_timeline_reference",
                columns: new[] { "UserId", "SubjectId", "EventId" },
                unique: true,
                filter: "\"EventId\" IS NOT NULL AND \"RemovedAt\" IS NULL");

            // Identity has its own database. Backfill users already known here; authenticated
            // initialization covers accounts without event data and newly registered accounts.
            migrationBuilder.Sql("""
                INSERT INTO subject ("Id", "UserId", "IsSelf", "Kind", "Name", "Timezone", "State", "FieldsJson", "ValuesJson", "MediaIdsJson", "Revision", "CreatedAt", "UpdatedAt")
                SELECT gen_random_uuid(), u.user_id, TRUE, 0, '自己', 'UTC', 0, '[]', '{}', '[]', 1, now(), now()
                FROM (SELECT user_id FROM trace_event UNION SELECT user_id FROM ai_conversation
                    UNION SELECT user_id FROM user_memory UNION SELECT user_id FROM media_asset) u
                ON CONFLICT ("UserId") WHERE "IsSelf" = TRUE DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "subject_history");

            migrationBuilder.DropTable(
                name: "subject_media_reference");

            migrationBuilder.DropTable(
                name: "subject_milestone");

            migrationBuilder.DropTable(
                name: "subject_relation");

            migrationBuilder.DropTable(
                name: "subject_timeline_reference");

            migrationBuilder.DropTable(
                name: "subject_entry");

            migrationBuilder.DropTable(
                name: "subject");

            migrationBuilder.DropColumn(
                name: "SubjectIdsJson",
                table: "trace_event");

            migrationBuilder.DropColumn(
                name: "SubjectIdsJson",
                table: "event_source_revision");
        }
    }
}
