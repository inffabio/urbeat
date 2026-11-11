using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Urbeat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPickupStoreAddressSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PickupStoreAddressCity",
                table: "Orders",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PickupStoreAddressComplement",
                table: "Orders",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PickupStoreAddressNeighborhood",
                table: "Orders",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PickupStoreAddressNumber",
                table: "Orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PickupStoreAddressReference",
                table: "Orders",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PickupStoreAddressState",
                table: "Orders",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PickupStoreAddressStreet",
                table: "Orders",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PickupStoreAddressZipCode",
                table: "Orders",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PickupStoreAddressCity",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PickupStoreAddressComplement",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PickupStoreAddressNeighborhood",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PickupStoreAddressNumber",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PickupStoreAddressReference",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PickupStoreAddressState",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PickupStoreAddressStreet",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PickupStoreAddressZipCode",
                table: "Orders");
        }
    }
}
