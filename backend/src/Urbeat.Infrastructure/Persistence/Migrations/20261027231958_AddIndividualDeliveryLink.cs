using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Urbeat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIndividualDeliveryLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeliveryLinkExpiresAtUtc",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryLinkTokenHash",
                table: "Orders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_DeliveryLinkTokenHash",
                table: "Orders",
                column: "DeliveryLinkTokenHash",
                unique: true,
                filter: "\"DeliveryLinkTokenHash\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_DeliveryLinkTokenHash",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "DeliveryLinkExpiresAtUtc",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "DeliveryLinkTokenHash",
                table: "Orders");
        }
    }
}
