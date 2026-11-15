using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenStore.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdminUser",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    EmailNormalized = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    FailedLoginCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LockoutUntil = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastLoginAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    PasswordChangedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminUser", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Session",
                columns: table => new
                {
                    TokenHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ActorType = table.Column<int>(type: "INTEGER", nullable: false),
                    ActorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    IdleExpiresAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    AbsoluteExpiresAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    IpHash = table.Column<string>(type: "TEXT", nullable: true),
                    UserAgentHash = table.Column<string>(type: "TEXT", nullable: true),
                    CsrfHash = table.Column<string>(type: "TEXT", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    RevokeReason = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Session", x => x.TokenHash);
                });

            migrationBuilder.CreateIndex(
                name: "UX_AdminUser_EmailNormalized",
                table: "AdminUser",
                column: "EmailNormalized",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Session_AbsoluteExpiresAt",
                table: "Session",
                column: "AbsoluteExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_Session_Actor",
                table: "Session",
                columns: new[] { "ActorType", "ActorId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminUser");

            migrationBuilder.DropTable(
                name: "Session");
        }
    }
}
