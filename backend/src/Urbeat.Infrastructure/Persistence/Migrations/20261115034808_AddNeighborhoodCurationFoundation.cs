using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Urbeat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNeighborhoodCurationFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "DeliveryNeighborhoods",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DeliveryNeighborhoodAliases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CityId = table.Column<Guid>(type: "uuid", nullable: false),
                    CanonicalNeighborhoodId = table.Column<Guid>(type: "uuid", nullable: false),
                    Alias = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    NormalizedAlias = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsApproved = table.Column<bool>(type: "boolean", nullable: false),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovalNotes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryNeighborhoodAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeliveryNeighborhoodAliases_Cities_CityId",
                        column: x => x.CityId,
                        principalTable: "Cities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryNeighborhoodAliases_DeliveryNeighborhoods_Canonical~",
                        column: x => x.CanonicalNeighborhoodId,
                        principalTable: "DeliveryNeighborhoods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNeighborhoodAliases_CanonicalNeighborhoodId_IsActive",
                table: "DeliveryNeighborhoodAliases",
                columns: new[] { "CanonicalNeighborhoodId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNeighborhoodAliases_CityId_NormalizedAlias",
                table: "DeliveryNeighborhoodAliases",
                columns: new[] { "CityId", "NormalizedAlias" },
                unique: true,
                filter: "\"IsActive\" = TRUE");

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION "ValidateDeliveryNeighborhoodAliasCity"()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM "DeliveryNeighborhoods" n
                        WHERE n."Id" = NEW."CanonicalNeighborhoodId"
                          AND n."CityId" = NEW."CityId"
                    ) THEN
                        RAISE EXCEPTION 'Delivery neighborhood alias city must match canonical neighborhood city';
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER "TR_DeliveryNeighborhoodAliases_CityScope"
                BEFORE INSERT OR UPDATE OF "CityId", "CanonicalNeighborhoodId"
                ON "DeliveryNeighborhoodAliases"
                FOR EACH ROW
                EXECUTE FUNCTION "ValidateDeliveryNeighborhoodAliasCity"();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeliveryNeighborhoodAliases");

            migrationBuilder.Sql("""
                DROP FUNCTION IF EXISTS "ValidateDeliveryNeighborhoodAliasCity"();
                """);

            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "DeliveryNeighborhoods");
        }
    }
}
