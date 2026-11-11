using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Urbeat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRolePromotionClaimLeaseAndCredentialForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClaimedBy",
                table: "RolePromotionRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_UserRoleCredentials_RolePromotionRequests",
                table: "UserRoleCredentials",
                column: "RolePromotionRequestId",
                principalTable: "RolePromotionRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserRoleCredentials_RolePromotionRequests",
                table: "UserRoleCredentials");

            migrationBuilder.DropColumn(
                name: "ClaimedBy",
                table: "RolePromotionRequests");
        }
    }
}
