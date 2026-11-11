using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Urbeat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRolePromotionEmailSendLease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RolePromotionRequests_EmailSentAtUtc_ExpiresAtUtc",
                table: "RolePromotionRequests");

            migrationBuilder.AddColumn<Guid>(
                name: "EmailSendAttemptId",
                table: "RolePromotionRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EmailSendingAtUtc",
                table: "RolePromotionRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RolePromotionRequests_EmailDeliverySweep",
                table: "RolePromotionRequests",
                columns: new[] { "EmailSentAtUtc", "EmailSendingAtUtc", "ExpiresAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RolePromotionRequests_EmailDeliverySweep",
                table: "RolePromotionRequests");

            migrationBuilder.DropColumn(
                name: "EmailSendAttemptId",
                table: "RolePromotionRequests");

            migrationBuilder.DropColumn(
                name: "EmailSendingAtUtc",
                table: "RolePromotionRequests");

            migrationBuilder.CreateIndex(
                name: "IX_RolePromotionRequests_EmailSentAtUtc_ExpiresAtUtc",
                table: "RolePromotionRequests",
                columns: new[] { "EmailSentAtUtc", "ExpiresAtUtc" });
        }
    }
}
