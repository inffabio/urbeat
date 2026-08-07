using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Urbeat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderItemCustomizations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProductId1",
                table: "ProductVariations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProductId1",
                table: "ProductChoiceOptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProductId1",
                table: "ProductAdditionals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdditionalNames",
                table: "OrderItems",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChoiceOptionName",
                table: "OrderItems",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "OrderItems",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VariationName",
                table: "OrderItems",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariations_ProductId1",
                table: "ProductVariations",
                column: "ProductId1");

            migrationBuilder.CreateIndex(
                name: "IX_ProductChoiceOptions_ProductId1",
                table: "ProductChoiceOptions",
                column: "ProductId1");

            migrationBuilder.CreateIndex(
                name: "IX_ProductAdditionals_ProductId1",
                table: "ProductAdditionals",
                column: "ProductId1");

            migrationBuilder.AddForeignKey(
                name: "FK_ProductAdditionals_Products_ProductId1",
                table: "ProductAdditionals",
                column: "ProductId1",
                principalTable: "Products",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ProductChoiceOptions_Products_ProductId1",
                table: "ProductChoiceOptions",
                column: "ProductId1",
                principalTable: "Products",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ProductVariations_Products_ProductId1",
                table: "ProductVariations",
                column: "ProductId1",
                principalTable: "Products",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductAdditionals_Products_ProductId1",
                table: "ProductAdditionals");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductChoiceOptions_Products_ProductId1",
                table: "ProductChoiceOptions");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductVariations_Products_ProductId1",
                table: "ProductVariations");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariations_ProductId1",
                table: "ProductVariations");

            migrationBuilder.DropIndex(
                name: "IX_ProductChoiceOptions_ProductId1",
                table: "ProductChoiceOptions");

            migrationBuilder.DropIndex(
                name: "IX_ProductAdditionals_ProductId1",
                table: "ProductAdditionals");

            migrationBuilder.DropColumn(
                name: "ProductId1",
                table: "ProductVariations");

            migrationBuilder.DropColumn(
                name: "ProductId1",
                table: "ProductChoiceOptions");

            migrationBuilder.DropColumn(
                name: "ProductId1",
                table: "ProductAdditionals");

            migrationBuilder.DropColumn(
                name: "AdditionalNames",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "ChoiceOptionName",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "VariationName",
                table: "OrderItems");
        }
    }
}
