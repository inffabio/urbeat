using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Urbeat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMotoboyRoutesAndStoreDriverContacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DeliveryDrivers_NormalizedEmail",
                table: "DeliveryDrivers");

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppPhoneNumber",
                table: "StoreDeliveryDrivers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedEmail",
                table: "DeliveryDrivers",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "DeliveryDrivers",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppPhoneNumber",
                table: "DeliveryDrivers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DeliveryRoutes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryDriverId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreDeliveryDriverId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConcurrencyVersion = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryRoutes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeliveryRoutes_DeliveryDrivers_DeliveryDriverId",
                        column: x => x.DeliveryDriverId,
                        principalTable: "DeliveryDrivers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryRoutes_StoreDeliveryDrivers_StoreDeliveryDriverId",
                        column: x => x.StoreDeliveryDriverId,
                        principalTable: "StoreDeliveryDrivers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryRoutes_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryRouteOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryRouteId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryRouteOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeliveryRouteOrders_DeliveryRoutes_DeliveryRouteId",
                        column: x => x.DeliveryRouteId,
                        principalTable: "DeliveryRoutes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DeliveryRouteOrders_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryDrivers_NormalizedEmail",
                table: "DeliveryDrivers",
                column: "NormalizedEmail",
                unique: true,
                filter: "\"NormalizedEmail\" IS NOT NULL AND \"NormalizedEmail\" <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryRouteOrders_DeliveryRouteId_OrderId",
                table: "DeliveryRouteOrders",
                columns: new[] { "DeliveryRouteId", "OrderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryRouteOrders_DeliveryRouteId_Sequence",
                table: "DeliveryRouteOrders",
                columns: new[] { "DeliveryRouteId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryRouteOrders_OrderId",
                table: "DeliveryRouteOrders",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryRoutes_DeliveryDriverId",
                table: "DeliveryRoutes",
                column: "DeliveryDriverId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryRoutes_StoreDeliveryDriverId",
                table: "DeliveryRoutes",
                column: "StoreDeliveryDriverId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryRoutes_StoreId_CreatedAtUtc",
                table: "DeliveryRoutes",
                columns: new[] { "StoreId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryRoutes_StoreId_DeliveryDriverId_RevokedAtUtc",
                table: "DeliveryRoutes",
                columns: new[] { "StoreId", "DeliveryDriverId", "RevokedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryRoutes_TokenHash",
                table: "DeliveryRoutes",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeliveryRouteOrders");

            migrationBuilder.DropTable(
                name: "DeliveryRoutes");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryDrivers_NormalizedEmail",
                table: "DeliveryDrivers");

            migrationBuilder.DropColumn(
                name: "WhatsAppPhoneNumber",
                table: "StoreDeliveryDrivers");

            migrationBuilder.DropColumn(
                name: "WhatsAppPhoneNumber",
                table: "DeliveryDrivers");

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedEmail",
                table: "DeliveryDrivers",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "DeliveryDrivers",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryDrivers_NormalizedEmail",
                table: "DeliveryDrivers",
                column: "NormalizedEmail",
                unique: true);
        }
    }
}
