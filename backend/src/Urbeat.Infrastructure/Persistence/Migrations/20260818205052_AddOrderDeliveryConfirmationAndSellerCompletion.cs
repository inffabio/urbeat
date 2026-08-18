using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Urbeat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderDeliveryConfirmationAndSellerCompletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeliveryConfirmedAtUtc",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SellerCompletedAtUtc",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeliveryConfirmedAtUtc",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "SellerCompletedAtUtc",
                table: "Orders");
        }
    }
}
