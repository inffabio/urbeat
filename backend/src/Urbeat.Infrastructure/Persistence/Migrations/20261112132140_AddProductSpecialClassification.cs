using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Urbeat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductSpecialClassification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SpecialClassification",
                table: "Products",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            // Backfill from the legacy organization flags without touching them. The precedence
            // (Offers > BestSeller > New) matches the approved product classification design.
            migrationBuilder.Sql(
                """
                UPDATE "Products"
                SET "SpecialClassification" = CASE
                    WHEN "IsFeatured" THEN 'Offers'
                    WHEN "IsBestSeller" THEN 'BestSeller'
                    WHEN "IsNew" THEN 'New'
                    ELSE NULL
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SpecialClassification",
                table: "Products");
        }
    }
}
