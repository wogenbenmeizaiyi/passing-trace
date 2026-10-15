using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PassingTrace.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSocialExperiences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ParticipantIdsJson",
                table: "event_source_revision",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "social_conversation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FriendshipId = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstUserId = table.Column<long>(type: "bigint", nullable: false),
                    SecondUserId = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_social_conversation", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "social_conversation_member",
                columns: table => new
                {
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    ReadThroughId = table.Column<long>(type: "bigint", nullable: false),
                    ClearedThroughId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_social_conversation_member", x => new { x.ConversationId, x.UserId });
                });

            migrationBuilder.CreateTable(
                name: "social_event_participant",
                columns: table => new
                {
                    EventId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    FriendshipId = table.Column<Guid>(type: "uuid", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    Declined = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_social_event_participant", x => new { x.EventId, x.UserId });
                    table.ForeignKey(
                        name: "FK_social_event_participant_trace_event_EventId",
                        column: x => x.EventId,
                        principalTable: "trace_event",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "social_friend_preference",
                columns: table => new
                {
                    FriendshipId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    Remark = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Label = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_social_friend_preference", x => new { x.FriendshipId, x.UserId });
                });

            migrationBuilder.CreateTable(
                name: "social_friend_request",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderId = table.Column<long>(type: "bigint", nullable: false),
                    RecipientId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_social_friend_request", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "social_friendship",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstUserId = table.Column<long>(type: "bigint", nullable: false),
                    SecondUserId = table.Column<long>(type: "bigint", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    Relationship = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ProposedRelationship = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    RelationshipRequestedBy = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_social_friendship", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "social_message",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderId = table.Column<long>(type: "bigint", nullable: false),
                    ClientMessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Text = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    ShareId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_social_message", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "social_notification",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Text = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Target = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Read = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_social_notification", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "social_share",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FriendshipId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<long>(type: "bigint", nullable: false),
                    RecipientId = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    EventId = table.Column<long>(type: "bigint", nullable: true),
                    StorylineId = table.Column<Guid>(type: "uuid", nullable: true),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    ContentJson = table.Column<string>(type: "text", nullable: false),
                    SearchText = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_social_share", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "social_user_block",
                columns: table => new
                {
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    BlockedUserId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_social_user_block", x => new { x.UserId, x.BlockedUserId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_social_conversation_FirstUserId_UpdatedAt",
                table: "social_conversation",
                columns: new[] { "FirstUserId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_social_conversation_FriendshipId",
                table: "social_conversation",
                column: "FriendshipId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_social_conversation_SecondUserId_UpdatedAt",
                table: "social_conversation",
                columns: new[] { "SecondUserId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_social_event_participant_UserId_Active_EventId",
                table: "social_event_participant",
                columns: new[] { "UserId", "Active", "EventId" });

            migrationBuilder.CreateIndex(
                name: "IX_social_friend_request_SenderId_RecipientId",
                table: "social_friend_request",
                columns: new[] { "SenderId", "RecipientId" },
                unique: true,
                filter: "\"Status\" = 'pending'");

            migrationBuilder.CreateIndex(
                name: "IX_social_friendship_FirstUserId_SecondUserId",
                table: "social_friendship",
                columns: new[] { "FirstUserId", "SecondUserId" },
                unique: true,
                filter: "\"Active\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_social_message_ConversationId_Id",
                table: "social_message",
                columns: new[] { "ConversationId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_social_message_SenderId_ClientMessageId",
                table: "social_message",
                columns: new[] { "SenderId", "ClientMessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_social_notification_UserId_Id",
                table: "social_notification",
                columns: new[] { "UserId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_social_share_OwnerId_EventId",
                table: "social_share",
                columns: new[] { "OwnerId", "EventId" });

            migrationBuilder.CreateIndex(
                name: "IX_social_share_RecipientId_CreatedAt",
                table: "social_share",
                columns: new[] { "RecipientId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "social_conversation");

            migrationBuilder.DropTable(
                name: "social_conversation_member");

            migrationBuilder.DropTable(
                name: "social_event_participant");

            migrationBuilder.DropTable(
                name: "social_friend_preference");

            migrationBuilder.DropTable(
                name: "social_friend_request");

            migrationBuilder.DropTable(
                name: "social_friendship");

            migrationBuilder.DropTable(
                name: "social_message");

            migrationBuilder.DropTable(
                name: "social_notification");

            migrationBuilder.DropTable(
                name: "social_share");

            migrationBuilder.DropTable(
                name: "social_user_block");

            migrationBuilder.DropColumn(
                name: "ParticipantIdsJson",
                table: "event_source_revision");
        }
    }
}
