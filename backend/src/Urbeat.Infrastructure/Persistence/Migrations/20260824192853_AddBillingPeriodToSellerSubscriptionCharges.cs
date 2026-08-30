using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Urbeat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingPeriodToSellerSubscriptionCharges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "BillingPeriodEndUtc",
                table: "SellerSubscriptionChargeHistories",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BillingPeriodStartUtc",
                table: "SellerSubscriptionChargeHistories",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "SellerSubscriptionChargeHistories"
                SET "BillingPeriodStartUtc" = "DueDateUtc",
                    "BillingPeriodEndUtc" = "DueDateUtc" + INTERVAL '1 month'
                WHERE "BillingPeriodStartUtc" IS NULL
                   OR "BillingPeriodEndUtc" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BillingPeriodEndUtc",
                table: "SellerSubscriptionChargeHistories");

            migrationBuilder.DropColumn(
                name: "BillingPeriodStartUtc",
                table: "SellerSubscriptionChargeHistories");
        }
    }
}
