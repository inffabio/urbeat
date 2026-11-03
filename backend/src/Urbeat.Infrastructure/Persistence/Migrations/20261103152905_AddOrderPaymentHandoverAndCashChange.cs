using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Urbeat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderPaymentHandoverAndCashChange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CashChangeAmount",
                table: "Orders",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CashChangeForAmount",
                table: "Orders",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CashReceivedAmount",
                table: "Orders",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CashWithoutChange",
                table: "Orders",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaymentConfirmedAtUtc",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PaymentConfirmedByDriverId",
                table: "Orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "HandoverConfirmedAtUtc",
                table: "OrderDeliveryAssignments",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CashChangeAmount",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CashChangeForAmount",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CashReceivedAmount",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CashWithoutChange",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentConfirmedAtUtc",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentConfirmedByDriverId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "HandoverConfirmedAtUtc",
                table: "OrderDeliveryAssignments");
        }
    }
}
