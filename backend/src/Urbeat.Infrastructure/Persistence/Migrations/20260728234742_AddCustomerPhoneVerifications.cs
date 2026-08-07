using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Urbeat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerPhoneVerifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerPhoneVerifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PendingCep = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    PendingStreet = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    PendingNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PendingComplement = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    PendingNeighborhood = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    PendingCity = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    PendingState = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResendAvailableAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConsumedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerPhoneVerifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerPhoneVerifications_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPhoneVerifications_StoreId",
                table: "CustomerPhoneVerifications",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPhoneVerifications_UserId",
                table: "CustomerPhoneVerifications",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerPhoneVerifications");

        }
    }
}
