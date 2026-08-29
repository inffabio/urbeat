using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Urbeat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderItemOptionPrices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OptionPricesJson",
                table: "OrderItems",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OptionPricesJson",
                table: "OrderItems");
        }
    }
}
