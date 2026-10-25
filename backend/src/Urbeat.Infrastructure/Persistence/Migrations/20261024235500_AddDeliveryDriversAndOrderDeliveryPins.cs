using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Urbeat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryDriversAndOrderDeliveryPins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeliveryPinConfirmedAtUtc",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeliveryPinConfirmedByDriverId",
                table: "Orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryPinEncrypted",
                table: "Orders",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryPinHash",
                table: "Orders",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DeliveryDrivers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    FullName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryDrivers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeliveryDrivers_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "OrderDeliveryAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryDriverId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeliveryStartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PinAttemptCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    LastPinAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderDeliveryAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderDeliveryAssignments_DeliveryDrivers_DeliveryDriverId",
                        column: x => x.DeliveryDriverId,
                        principalTable: "DeliveryDrivers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderDeliveryAssignments_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StoreDeliveryDrivers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryDriverId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    InviteTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    InviteExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    InviteSentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    InviteVersion = table.Column<int>(type: "integer", nullable: false),
                    AcceptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    InvitedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreDeliveryDrivers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoreDeliveryDrivers_DeliveryDrivers_DeliveryDriverId",
                        column: x => x.DeliveryDriverId,
                        principalTable: "DeliveryDrivers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StoreDeliveryDrivers_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryDrivers_NormalizedEmail",
                table: "DeliveryDrivers",
                column: "NormalizedEmail",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryDrivers_UserId",
                table: "DeliveryDrivers",
                column: "UserId",
                unique: true,
                filter: "\"UserId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OrderDeliveryAssignments_DeliveryDriverId",
                table: "OrderDeliveryAssignments",
                column: "DeliveryDriverId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderDeliveryAssignments_OrderId",
                table: "OrderDeliveryAssignments",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderDeliveryAssignments_StoreId",
                table: "OrderDeliveryAssignments",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreDeliveryDrivers_DeliveryDriverId",
                table: "StoreDeliveryDrivers",
                column: "DeliveryDriverId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreDeliveryDrivers_InviteTokenHash",
                table: "StoreDeliveryDrivers",
                column: "InviteTokenHash",
                unique: true,
                filter: "\"InviteTokenHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StoreDeliveryDrivers_StoreId_DeliveryDriverId",
                table: "StoreDeliveryDrivers",
                columns: new[] { "StoreId", "DeliveryDriverId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderDeliveryAssignments");

            migrationBuilder.DropTable(
                name: "StoreDeliveryDrivers");

            migrationBuilder.DropTable(
                name: "DeliveryDrivers");

            migrationBuilder.DropColumn(
                name: "DeliveryPinConfirmedAtUtc",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "DeliveryPinConfirmedByDriverId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "DeliveryPinEncrypted",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "DeliveryPinHash",
                table: "Orders");
        }
    }
}
