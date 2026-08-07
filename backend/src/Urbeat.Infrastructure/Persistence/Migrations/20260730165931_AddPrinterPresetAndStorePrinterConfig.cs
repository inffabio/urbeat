using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Urbeat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPrinterPresetAndStorePrinterConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PrinterPresets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Manufacturer = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ConnectionType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    PaperWidth = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    CommandSet = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AdapterId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrinterPresets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StorePrinterConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrinterPresetId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrinterName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    MacAddress = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Copies = table.Column<int>(type: "integer", nullable: false),
                    AutoPrint = table.Column<bool>(type: "boolean", nullable: false),
                    AutoCut = table.Column<bool>(type: "boolean", nullable: false),
                    PrintKitchenCopy = table.Column<bool>(type: "boolean", nullable: false),
                    PrintCounterCopy = table.Column<bool>(type: "boolean", nullable: false),
                    PrintCustomerReceipt = table.Column<bool>(type: "boolean", nullable: false),
                    PrintLogo = table.Column<bool>(type: "boolean", nullable: false),
                    HighlightOrderNumber = table.Column<bool>(type: "boolean", nullable: false),
                    FooterText = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorePrinterConfigs", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "PrinterPresets",
                columns: new[] { "Id", "AdapterId", "CommandSet", "ConnectionType", "CreatedAtUtc", "Description", "IsActive", "Manufacturer", "Name", "PaperWidth", "UpdatedAtUtc" },
                values: new object[,]
                {
                    { new Guid("c1000000-0000-0000-0000-000000000001"), "escpos-bluetooth", "esc-pos", "android-bluetooth", new DateTime(2026, 7, 30, 0, 0, 0, 0, DateTimeKind.Utc), "Impressora termica chinesa compacta 58mm. Bluetooth classico (SPP) via app Android/Capacitor.", true, "Havendo", "Mini Thermal Printer TC-163", "58mm", null },
                    { new Guid("c1000000-0000-0000-0000-000000000002"), "escpos-bluetooth", "esc-pos", "android-bluetooth", new DateTime(2026, 7, 30, 0, 0, 0, 0, DateTimeKind.Utc), "Modelo base para impressoras termicas 58mm compativeis com ESC/POS.", true, "Generica", "ESC/POS 58mm generica", "58mm", null },
                    { new Guid("c1000000-0000-0000-0000-000000000003"), "escpos-bluetooth", "esc-pos", "android-bluetooth", new DateTime(2026, 7, 30, 0, 0, 0, 0, DateTimeKind.Utc), "Modelo base para impressoras termicas 80mm compativeis com ESC/POS.", true, "Generica", "ESC/POS 80mm generica", "80mm", null },
                    { new Guid("c1000000-0000-0000-0000-000000000004"), "browser-print", "browser", "browser-print", new DateTime(2026, 7, 30, 0, 0, 0, 0, DateTimeKind.Utc), "Usa a janela de impressao do navegador. Dispensa hardware dedicado.", true, "Sistema", "Impressora do navegador", "80mm", null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrinterPresets_AdapterId",
                table: "PrinterPresets",
                column: "AdapterId");

            migrationBuilder.CreateIndex(
                name: "IX_StorePrinterConfigs_StoreId",
                table: "StorePrinterConfigs",
                column: "StoreId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PrinterPresets");

            migrationBuilder.DropTable(
                name: "StorePrinterConfigs");
        }
    }
}
