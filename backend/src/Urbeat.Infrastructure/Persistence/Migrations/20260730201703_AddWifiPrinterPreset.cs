using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Urbeat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWifiPrinterPreset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "PrinterPresets",
                columns: new[] { "Id", "AdapterId", "CommandSet", "ConnectionType", "CreatedAtUtc", "Description", "IsActive", "Manufacturer", "Name", "PaperWidth", "UpdatedAtUtc" },
                values: new object[] { new Guid("c1000000-0000-0000-0000-000000000005"), "wifi-escpos", "esc-pos", "wifi", new DateTime(2026, 7, 30, 0, 0, 0, 0, DateTimeKind.Utc), "Impressora termica 58mm via Wi-Fi (rede local). Mesmos comandos ESC/POS, conexao por IP. Funciona em Android, iOS, Windows e macOS.", true, "Generica", "ESC/POS Wi-Fi 58mm", "58mm", null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "PrinterPresets",
                keyColumn: "Id",
                keyValue: new Guid("c1000000-0000-0000-0000-000000000005"));
        }
    }
}
