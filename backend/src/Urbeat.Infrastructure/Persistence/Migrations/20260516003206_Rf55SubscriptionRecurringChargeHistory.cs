using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Urbeat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Rf55SubscriptionRecurringChargeHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SellerSubscriptionChargeHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SellerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    GatewayChargeId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ExternalReference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    GatewayStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    BillingStatus = table.Column<int>(type: "integer", nullable: false),
                    DueDateUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PaidAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    RawPayload = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SellerSubscriptionChargeHistories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SellerSubscriptionChargeHistories_DueDateUtc",
                table: "SellerSubscriptionChargeHistories",
                column: "DueDateUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SellerSubscriptionChargeHistories_GatewayChargeId",
                table: "SellerSubscriptionChargeHistories",
                column: "GatewayChargeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SellerSubscriptionChargeHistories_SellerUserId",
                table: "SellerSubscriptionChargeHistories",
                column: "SellerUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SellerSubscriptionChargeHistories");
        }
    }
}
