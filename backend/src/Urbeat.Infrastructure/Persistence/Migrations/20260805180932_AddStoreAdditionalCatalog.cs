using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Urbeat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStoreAdditionalCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "StoreAdditionalId",
                table: "ProductAdditional",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StoreAdditionalGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreAdditionalGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoreAdditionalGroups_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StoreAdditionals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Price = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreAdditionals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoreAdditionals_StoreAdditionalGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "StoreAdditionalGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StoreAdditionals_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductAdditionalAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdditionalId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductAdditionalAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductAdditionalAssignments_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductAdditionalAssignments_StoreAdditionals_AdditionalId",
                        column: x => x.AdditionalId,
                        principalTable: "StoreAdditionals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductAdditional_StoreAdditionalId",
                table: "ProductAdditional",
                column: "StoreAdditionalId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductAdditionalAssignments_AdditionalId",
                table: "ProductAdditionalAssignments",
                column: "AdditionalId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductAdditionalAssignments_ProductId_AdditionalId",
                table: "ProductAdditionalAssignments",
                columns: new[] { "ProductId", "AdditionalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoreAdditionalGroups_StoreId_Name",
                table: "StoreAdditionalGroups",
                columns: new[] { "StoreId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoreAdditionals_GroupId",
                table: "StoreAdditionals",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreAdditionals_StoreId_Name",
                table: "StoreAdditionals",
                columns: new[] { "StoreId", "Name" });

            migrationBuilder.AddForeignKey(
                name: "FK_ProductAdditional_StoreAdditionals_StoreAdditionalId",
                table: "ProductAdditional",
                column: "StoreAdditionalId",
                principalTable: "StoreAdditionals",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductAdditional_StoreAdditionals_StoreAdditionalId",
                table: "ProductAdditional");

            migrationBuilder.DropTable(
                name: "ProductAdditionalAssignments");

            migrationBuilder.DropTable(
                name: "StoreAdditionals");

            migrationBuilder.DropTable(
                name: "StoreAdditionalGroups");

            migrationBuilder.DropIndex(
                name: "IX_ProductAdditional_StoreAdditionalId",
                table: "ProductAdditional");

            migrationBuilder.DropColumn(
                name: "StoreAdditionalId",
                table: "ProductAdditional");
        }
    }
}
