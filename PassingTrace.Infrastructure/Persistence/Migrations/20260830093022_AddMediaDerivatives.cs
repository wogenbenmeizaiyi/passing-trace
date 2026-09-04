using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PassingTrace.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaDerivatives : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ai_object_key",
                table: "media_asset",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "thumbnail_object_key",
                table: "media_asset",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ai_object_key",
                table: "media_asset");

            migrationBuilder.DropColumn(
                name: "thumbnail_object_key",
                table: "media_asset");
        }
    }
}
