using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Urbeat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSaleModeWeightVariationsAndSimplifyOptionGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisplayStyle",
                table: "ProductOptionGroups");

            migrationBuilder.DropColumn(
                name: "PriceMode",
                table: "ProductOptionGroups");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "ProductVariation",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDefault",
                table: "ProductVariation",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "WeightGrams",
                table: "ProductVariation",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SaleMode",
                table: "Products",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "single");

            migrationBuilder.AddColumn<int>(
                name: "WeightGrams",
                table: "OrderItems",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProductWeightConfig",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    PricePerKg = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    MinGrams = table.Column<int>(type: "integer", nullable: false),
                    MaxGrams = table.Column<int>(type: "integer", nullable: false),
                    IncrementGrams = table.Column<int>(type: "integer", nullable: false),
                    IsEstimated = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductWeightConfig", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductWeightConfig_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductWeightConfig_ProductId",
                table: "ProductWeightConfig",
                column: "ProductId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductWeightConfig");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "ProductVariation");

            migrationBuilder.DropColumn(
                name: "IsDefault",
                table: "ProductVariation");

            migrationBuilder.DropColumn(
                name: "WeightGrams",
                table: "ProductVariation");

            migrationBuilder.DropColumn(
                name: "SaleMode",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "WeightGrams",
                table: "OrderItems");

            migrationBuilder.AddColumn<string>(
                name: "DisplayStyle",
                table: "ProductOptionGroups",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PriceMode",
                table: "ProductOptionGroups",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}
