using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PassingTrace.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFriendCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "friend_code",
                table: "identity_user",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("UPDATE identity_user SET friend_code = upper(substr(replace(gen_random_uuid()::text, '-', ''), 1, 16)) WHERE friend_code = '';");

            migrationBuilder.CreateIndex(
                name: "IX_identity_user_friend_code",
                table: "identity_user",
                column: "friend_code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_identity_user_friend_code",
                table: "identity_user");

            migrationBuilder.DropColumn(
                name: "friend_code",
                table: "identity_user");
        }
    }
}
