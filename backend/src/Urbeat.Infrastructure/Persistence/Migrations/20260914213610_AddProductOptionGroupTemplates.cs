using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Urbeat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductOptionGroupTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TemplateId",
                table: "ProductOptionGroups",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProductOptionGroupTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    ChoiceType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "single"),
                    MinChoices = table.Column<int>(type: "integer", nullable: false),
                    MaxChoices = table.Column<int>(type: "integer", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductOptionGroupTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductOptionGroupTemplates_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductOptionItemTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Price = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductOptionItemTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductOptionItemTemplates_ProductOptionGroupTemplates_Grou~",
                        column: x => x.GroupId,
                        principalTable: "ProductOptionGroupTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductOptionGroups_TemplateId",
                table: "ProductOptionGroups",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductOptionGroupTemplates_StoreId_Name",
                table: "ProductOptionGroupTemplates",
                columns: new[] { "StoreId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductOptionItemTemplates_GroupId",
                table: "ProductOptionItemTemplates",
                column: "GroupId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProductOptionGroups_ProductOptionGroupTemplates_TemplateId",
                table: "ProductOptionGroups",
                column: "TemplateId",
                principalTable: "ProductOptionGroupTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductOptionGroups_ProductOptionGroupTemplates_TemplateId",
                table: "ProductOptionGroups");

            migrationBuilder.DropTable(
                name: "ProductOptionItemTemplates");

            migrationBuilder.DropTable(
                name: "ProductOptionGroupTemplates");

            migrationBuilder.DropIndex(
                name: "IX_ProductOptionGroups_TemplateId",
                table: "ProductOptionGroups");

            migrationBuilder.DropColumn(
                name: "TemplateId",
                table: "ProductOptionGroups");
        }
    }
}
