using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PassingTrace.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSemanticRelationships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_event_search_index_trgm",
                table: "event_search_index",
                column: "retrieval_text")
                .Annotation("Npgsql:IndexMethod", "GIN")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.AddForeignKey(
                name: "FK_event_semantic_run_trace_event_event_id",
                table: "event_semantic_run",
                column: "event_id",
                principalTable: "trace_event",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_outbox_message_trace_event_event_id",
                table: "outbox_message",
                column: "event_id",
                principalTable: "trace_event",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_event_semantic_run_trace_event_event_id",
                table: "event_semantic_run");

            migrationBuilder.DropForeignKey(
                name: "FK_outbox_message_trace_event_event_id",
                table: "outbox_message");

            migrationBuilder.DropIndex(
                name: "ix_event_search_index_trgm",
                table: "event_search_index");
        }
    }
}
