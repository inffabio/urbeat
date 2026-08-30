using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Urbeat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HonestOutboxOrderingAndConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyStamp",
                table: "Payments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<long>(
                name: "Sequence",
                table: "OutboxMessages",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "StatusVersion",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_AggregateId_Sequence",
                table: "OutboxMessages",
                columns: new[] { "AggregateId", "Sequence" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_AggregateId_Sequence",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "ConcurrencyStamp",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "Sequence",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "StatusVersion",
                table: "Orders");
        }
    }
}
