using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Urbeat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HardenStoreAdditionalAndCategoryOrdering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StoreAdditionals_StoreId_Name",
                table: "StoreAdditionals");

            migrationBuilder.CreateIndex(
                name: "IX_StoreAdditionals_StoreId_GroupId_Name",
                table: "StoreAdditionals",
                columns: new[] { "StoreId", "GroupId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StoreAdditionals_StoreId_GroupId_Name",
                table: "StoreAdditionals");

            migrationBuilder.CreateIndex(
                name: "IX_StoreAdditionals_StoreId_Name",
                table: "StoreAdditionals",
                columns: new[] { "StoreId", "Name" });
        }
    }
}
