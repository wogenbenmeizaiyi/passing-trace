using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PassingTrace.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "avatar_key",
                table: "identity_user",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bio",
                table: "identity_user",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "nickname",
                table: "identity_user",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "profile_version",
                table: "identity_user",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "avatar_key",
                table: "identity_user");

            migrationBuilder.DropColumn(
                name: "bio",
                table: "identity_user");

            migrationBuilder.DropColumn(
                name: "nickname",
                table: "identity_user");

            migrationBuilder.DropColumn(
                name: "profile_version",
                table: "identity_user");
        }
    }
}
