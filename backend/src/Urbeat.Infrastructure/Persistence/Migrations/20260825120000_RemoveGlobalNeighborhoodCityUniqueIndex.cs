using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Urbeat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveGlobalNeighborhoodCityUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DeliveryNeighborhoods_Neighborhood_City",
                table: "DeliveryNeighborhoods");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNeighborhoods_Neighborhood_City",
                table: "DeliveryNeighborhoods",
                columns: new[] { "Neighborhood", "City" },
                unique: true);
        }
    }
}
