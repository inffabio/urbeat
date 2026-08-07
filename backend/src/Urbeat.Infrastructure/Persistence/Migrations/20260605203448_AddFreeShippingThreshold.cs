using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Urbeat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFreeShippingThreshold : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductAdditionals_Products_ProductId",
                table: "ProductAdditionals");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductAdditionals_Products_ProductId1",
                table: "ProductAdditionals");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductChoiceOptions_Products_ProductId",
                table: "ProductChoiceOptions");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductChoiceOptions_Products_ProductId1",
                table: "ProductChoiceOptions");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductVariations_Products_ProductId",
                table: "ProductVariations");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductVariations_Products_ProductId1",
                table: "ProductVariations");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ProductVariations",
                table: "ProductVariations");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariations_ProductId1",
                table: "ProductVariations");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ProductChoiceOptions",
                table: "ProductChoiceOptions");

            migrationBuilder.DropIndex(
                name: "IX_ProductChoiceOptions_ProductId1",
                table: "ProductChoiceOptions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ProductAdditionals",
                table: "ProductAdditionals");

            migrationBuilder.DropIndex(
                name: "IX_ProductAdditionals_ProductId1",
                table: "ProductAdditionals");

            migrationBuilder.DropColumn(
                name: "CuisineType",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "EstimatedDeliveryTime",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "ProductId1",
                table: "ProductVariations");

            migrationBuilder.DropColumn(
                name: "ProductId1",
                table: "ProductChoiceOptions");

            migrationBuilder.DropColumn(
                name: "ProductId1",
                table: "ProductAdditionals");

            migrationBuilder.RenameTable(
                name: "ProductVariations",
                newName: "ProductVariation");

            migrationBuilder.RenameTable(
                name: "ProductChoiceOptions",
                newName: "ProductChoiceOption");

            migrationBuilder.RenameTable(
                name: "ProductAdditionals",
                newName: "ProductAdditional");

            migrationBuilder.RenameIndex(
                name: "IX_ProductVariations_ProductId",
                table: "ProductVariation",
                newName: "IX_ProductVariation_ProductId");

            migrationBuilder.RenameIndex(
                name: "IX_ProductChoiceOptions_ProductId",
                table: "ProductChoiceOption",
                newName: "IX_ProductChoiceOption_ProductId");

            migrationBuilder.RenameIndex(
                name: "IX_ProductAdditionals_ProductId",
                table: "ProductAdditional",
                newName: "IX_ProductAdditional_ProductId");

            migrationBuilder.AddColumn<Guid>(
                name: "CuisineTypeId",
                table: "Stores",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeliveryTimeId",
                table: "Stores",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "PromotionalPrice",
                table: "Products",
                type: "numeric",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,2)",
                oldPrecision: 10,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "ProductCategories",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "PromotionalPrice",
                table: "ProductVariation",
                type: "numeric",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,2)",
                oldPrecision: 10,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Price",
                table: "ProductVariation",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,2)",
                oldPrecision: 10,
                oldScale: 2);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "ProductVariation",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120);

            migrationBuilder.AlterColumn<decimal>(
                name: "Price",
                table: "ProductChoiceOption",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,2)",
                oldPrecision: 10,
                oldScale: 2);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "ProductChoiceOption",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120);

            migrationBuilder.AlterColumn<decimal>(
                name: "Price",
                table: "ProductAdditional",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,2)",
                oldPrecision: 10,
                oldScale: 2);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "ProductAdditional",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120);

            migrationBuilder.AddPrimaryKey(
                name: "PK_ProductVariation",
                table: "ProductVariation",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ProductChoiceOption",
                table: "ProductChoiceOption",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ProductAdditional",
                table: "ProductAdditional",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "DeliveryTime",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MinTimeMinutes = table.Column<int>(type: "integer", nullable: false),
                    MaxTimeMinutes = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryTime", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Stores_CuisineTypeId",
                table: "Stores",
                column: "CuisineTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Stores_DeliveryTimeId",
                table: "Stores",
                column: "DeliveryTimeId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProductAdditional_Products_ProductId",
                table: "ProductAdditional",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductChoiceOption_Products_ProductId",
                table: "ProductChoiceOption",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductVariation_Products_ProductId",
                table: "ProductVariation",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Stores_CuisineTypes_CuisineTypeId",
                table: "Stores",
                column: "CuisineTypeId",
                principalTable: "CuisineTypes",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Stores_DeliveryTime_DeliveryTimeId",
                table: "Stores",
                column: "DeliveryTimeId",
                principalTable: "DeliveryTime",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductAdditional_Products_ProductId",
                table: "ProductAdditional");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductChoiceOption_Products_ProductId",
                table: "ProductChoiceOption");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductVariation_Products_ProductId",
                table: "ProductVariation");

            migrationBuilder.DropForeignKey(
                name: "FK_Stores_CuisineTypes_CuisineTypeId",
                table: "Stores");

            migrationBuilder.DropForeignKey(
                name: "FK_Stores_DeliveryTime_DeliveryTimeId",
                table: "Stores");

            migrationBuilder.DropTable(
                name: "DeliveryTime");

            migrationBuilder.DropIndex(
                name: "IX_Stores_CuisineTypeId",
                table: "Stores");

            migrationBuilder.DropIndex(
                name: "IX_Stores_DeliveryTimeId",
                table: "Stores");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ProductVariation",
                table: "ProductVariation");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ProductChoiceOption",
                table: "ProductChoiceOption");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ProductAdditional",
                table: "ProductAdditional");

            migrationBuilder.DropColumn(
                name: "CuisineTypeId",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "DeliveryTimeId",
                table: "Stores");

            migrationBuilder.RenameTable(
                name: "ProductVariation",
                newName: "ProductVariations");

            migrationBuilder.RenameTable(
                name: "ProductChoiceOption",
                newName: "ProductChoiceOptions");

            migrationBuilder.RenameTable(
                name: "ProductAdditional",
                newName: "ProductAdditionals");

            migrationBuilder.RenameIndex(
                name: "IX_ProductVariation_ProductId",
                table: "ProductVariations",
                newName: "IX_ProductVariations_ProductId");

            migrationBuilder.RenameIndex(
                name: "IX_ProductChoiceOption_ProductId",
                table: "ProductChoiceOptions",
                newName: "IX_ProductChoiceOptions_ProductId");

            migrationBuilder.RenameIndex(
                name: "IX_ProductAdditional_ProductId",
                table: "ProductAdditionals",
                newName: "IX_ProductAdditionals_ProductId");

            migrationBuilder.AddColumn<string>(
                name: "CuisineType",
                table: "Stores",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EstimatedDeliveryTime",
                table: "Stores",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<decimal>(
                name: "PromotionalPrice",
                table: "Products",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "ProductCategories",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "PromotionalPrice",
                table: "ProductVariations",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Price",
                table: "ProductVariations",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "ProductVariations",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<Guid>(
                name: "ProductId1",
                table: "ProductVariations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Price",
                table: "ProductChoiceOptions",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "ProductChoiceOptions",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<Guid>(
                name: "ProductId1",
                table: "ProductChoiceOptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Price",
                table: "ProductAdditionals",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "ProductAdditionals",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<Guid>(
                name: "ProductId1",
                table: "ProductAdditionals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_ProductVariations",
                table: "ProductVariations",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ProductChoiceOptions",
                table: "ProductChoiceOptions",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ProductAdditionals",
                table: "ProductAdditionals",
                column: "Id");

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
                name: "FK_ProductAdditionals_Products_ProductId",
                table: "ProductAdditionals",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductAdditionals_Products_ProductId1",
                table: "ProductAdditionals",
                column: "ProductId1",
                principalTable: "Products",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ProductChoiceOptions_Products_ProductId",
                table: "ProductChoiceOptions",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductChoiceOptions_Products_ProductId1",
                table: "ProductChoiceOptions",
                column: "ProductId1",
                principalTable: "Products",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ProductVariations_Products_ProductId",
                table: "ProductVariations",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductVariations_Products_ProductId1",
                table: "ProductVariations",
                column: "ProductId1",
                principalTable: "Products",
                principalColumn: "Id");
        }
    }
}
