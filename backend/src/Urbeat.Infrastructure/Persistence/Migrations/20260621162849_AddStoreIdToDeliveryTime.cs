using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Urbeat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStoreIdToDeliveryTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Stores_DeliveryTime_DeliveryTimeId",
                table: "Stores");

            migrationBuilder.DropIndex(
                name: "IX_Stores_DeliveryTimeId",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "DeliveryTimeId",
                table: "Stores");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DeliveryTimeId",
                table: "Stores",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stores_DeliveryTimeId",
                table: "Stores",
                column: "DeliveryTimeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Stores_DeliveryTime_DeliveryTimeId",
                table: "Stores",
                column: "DeliveryTimeId",
                principalTable: "DeliveryTime",
                principalColumn: "Id");
        }
    }
}
