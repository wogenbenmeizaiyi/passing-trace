using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PassingTrace.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MobileRegistrationAndQrLogin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "identity_mobile_authorization_ticket",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketHash = table.Column<string>(type: "character varying(43)", maxLength: 43, nullable: false),
                    TicketType = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: true),
                    ClientId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RedirectUri = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CodeChallenge = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    State = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Nonce = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    NormalizedUsernameHash = table.Column<string>(type: "character varying(43)", maxLength: 43, nullable: true),
                    RequestHash = table.Column<string>(type: "character varying(43)", maxLength: 43, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_mobile_authorization_ticket", x => x.Id);
                    table.ForeignKey(
                        name: "FK_identity_mobile_authorization_ticket_identity_user_UserId",
                        column: x => x.UserId,
                        principalTable: "identity_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "identity_mobile_device",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SecretHash = table.Column<string>(type: "character varying(43)", maxLength: 43, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_mobile_device", x => x.Id);
                    table.ForeignKey(
                        name: "FK_identity_mobile_device_identity_user_UserId",
                        column: x => x.UserId,
                        principalTable: "identity_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "identity_qr_login_transaction",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(43)", maxLength: 43, nullable: false),
                    BrowserBindingHash = table.Column<string>(type: "character varying(43)", maxLength: 43, nullable: false),
                    ClientId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProtectedAuthorizeRequest = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ApprovedUserId = table.Column<long>(type: "bigint", nullable: true),
                    SourceIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserAgent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RejectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_qr_login_transaction", x => x.Id);
                    table.ForeignKey(
                        name: "FK_identity_qr_login_transaction_identity_user_ApprovedUserId",
                        column: x => x.ApprovedUserId,
                        principalTable: "identity_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_identity_mobile_authorization_ticket_ExpiresAt",
                table: "identity_mobile_authorization_ticket",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_identity_mobile_authorization_ticket_TicketHash",
                table: "identity_mobile_authorization_ticket",
                column: "TicketHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_identity_mobile_authorization_ticket_UserId",
                table: "identity_mobile_authorization_ticket",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_identity_mobile_device_SecretHash",
                table: "identity_mobile_device",
                column: "SecretHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_identity_mobile_device_UserId_RevokedAt",
                table: "identity_mobile_device",
                columns: new[] { "UserId", "RevokedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_identity_qr_login_transaction_ApprovedUserId",
                table: "identity_qr_login_transaction",
                column: "ApprovedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_identity_qr_login_transaction_CodeHash",
                table: "identity_qr_login_transaction",
                column: "CodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_identity_qr_login_transaction_Status_ExpiresAt",
                table: "identity_qr_login_transaction",
                columns: new[] { "Status", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "identity_mobile_authorization_ticket");

            migrationBuilder.DropTable(
                name: "identity_mobile_device");

            migrationBuilder.DropTable(
                name: "identity_qr_login_transaction");
        }
    }
}
