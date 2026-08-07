using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Urbeat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCityAndNeighborhoodOsmFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdminLevel",
                table: "DeliveryNeighborhoods",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Boundary",
                table: "DeliveryNeighborhoods",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CityId",
                table: "DeliveryNeighborhoods",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Latitude",
                table: "DeliveryNeighborhoods",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitude",
                table: "DeliveryNeighborhoods",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedName",
                table: "DeliveryNeighborhoods",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OsmId",
                table: "DeliveryNeighborhoods",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OsmType",
                table: "DeliveryNeighborhoods",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlaceType",
                table: "DeliveryNeighborhoods",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "DeliveryNeighborhoods",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Cities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Uf = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    IbgeCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    OsmId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    OsmAreaId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cities", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNeighborhoods_CityId_NormalizedName",
                table: "DeliveryNeighborhoods",
                columns: new[] { "CityId", "NormalizedName" },
                unique: true,
                filter: "\"CityId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Cities_Name_Uf",
                table: "Cities",
                columns: new[] { "Name", "Uf" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cities_Uf_IbgeCode",
                table: "Cities",
                columns: new[] { "Uf", "IbgeCode" },
                unique: true,
                filter: "\"IbgeCode\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_DeliveryNeighborhoods_Cities_CityId",
                table: "DeliveryNeighborhoods",
                column: "CityId",
                principalTable: "Cities",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DeliveryNeighborhoods_Cities_CityId",
                table: "DeliveryNeighborhoods");

            migrationBuilder.DropTable(
                name: "Cities");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryNeighborhoods_CityId_NormalizedName",
                table: "DeliveryNeighborhoods");

            migrationBuilder.DropColumn(
                name: "AdminLevel",
                table: "DeliveryNeighborhoods");

            migrationBuilder.DropColumn(
                name: "Boundary",
                table: "DeliveryNeighborhoods");

            migrationBuilder.DropColumn(
                name: "CityId",
                table: "DeliveryNeighborhoods");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "DeliveryNeighborhoods");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "DeliveryNeighborhoods");

            migrationBuilder.DropColumn(
                name: "NormalizedName",
                table: "DeliveryNeighborhoods");

            migrationBuilder.DropColumn(
                name: "OsmId",
                table: "DeliveryNeighborhoods");

            migrationBuilder.DropColumn(
                name: "OsmType",
                table: "DeliveryNeighborhoods");

            migrationBuilder.DropColumn(
                name: "PlaceType",
                table: "DeliveryNeighborhoods");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "DeliveryNeighborhoods");
        }
    }
}
