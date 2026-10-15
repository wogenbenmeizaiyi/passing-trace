using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PassingTrace.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiMutationOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_mutation_operation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_message_id = table.Column<long>(type: "bigint", nullable: false),
                    operation_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    operation = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    target_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    target_id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    expected_version = table.Column<long>(type: "bigint", nullable: true),
                    state = table.Column<int>(type: "integer", nullable: false),
                    result = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_mutation_operation", x => x.id);
                    table.ForeignKey(
                        name: "FK_ai_mutation_operation_ai_conversation_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "ai_conversation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ai_mutation_operation_ai_message_source_message_id",
                        column: x => x.source_message_id,
                        principalTable: "ai_message",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ai_mutation_operation_conversation_id",
                table: "ai_mutation_operation",
                column: "conversation_id");

            migrationBuilder.CreateIndex(
                name: "IX_ai_mutation_operation_source_message_id",
                table: "ai_mutation_operation",
                column: "source_message_id");

            migrationBuilder.CreateIndex(
                name: "IX_ai_mutation_operation_user_id_conversation_id_state_created~",
                table: "ai_mutation_operation",
                columns: new[] { "user_id", "conversation_id", "state", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_ai_mutation_operation_user_id_operation_key",
                table: "ai_mutation_operation",
                columns: new[] { "user_id", "operation_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_mutation_operation");
        }
    }
}
