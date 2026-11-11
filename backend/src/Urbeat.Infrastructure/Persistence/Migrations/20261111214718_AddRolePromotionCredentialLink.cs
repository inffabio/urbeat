using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Urbeat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRolePromotionCredentialLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RolePromotionRequestId",
                table: "UserRoleCredentials",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserRoleCredentials_RolePromotionRequestId",
                table: "UserRoleCredentials",
                column: "RolePromotionRequestId",
                unique: true,
                filter: "\"RolePromotionRequestId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserRoleCredentials_RolePromotionRequestId",
                table: "UserRoleCredentials");

            migrationBuilder.DropColumn(
                name: "RolePromotionRequestId",
                table: "UserRoleCredentials");
        }
    }
}
