using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Urbeat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Rf69SellerSubscriptionNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SellerSubscriptionStatuses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SellerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    NextDueDateUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BillingStatus = table.Column<int>(type: "integer", nullable: false),
                    LastNotifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SellerSubscriptionStatuses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SellerSubscriptionStatuses_NextDueDateUtc",
                table: "SellerSubscriptionStatuses",
                column: "NextDueDateUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SellerSubscriptionStatuses_SellerUserId",
                table: "SellerSubscriptionStatuses",
                column: "SellerUserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SellerSubscriptionStatuses");
        }
    }
}
