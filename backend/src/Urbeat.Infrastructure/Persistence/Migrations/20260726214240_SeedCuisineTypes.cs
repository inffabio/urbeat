using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Urbeat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedCuisineTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "CuisineTypes",
                columns: new[] { "Id", "CreatedAtUtc", "IsActive", "Name", "UpdatedAtUtc" },
                values: new object[,]
                {
                    { new Guid("b1000000-0000-0000-0000-000000000001"), new DateTime(2026, 7, 26, 21, 42, 39, 643, DateTimeKind.Utc).AddTicks(2478), true, "Acaiteria", null },
                    { new Guid("b1000000-0000-0000-0000-000000000002"), new DateTime(2026, 7, 26, 21, 42, 39, 643, DateTimeKind.Utc).AddTicks(2789), true, "Cachorro Quente", null },
                    { new Guid("b1000000-0000-0000-0000-000000000003"), new DateTime(2026, 7, 26, 21, 42, 39, 643, DateTimeKind.Utc).AddTicks(2791), true, "Comida Arabe", null },
                    { new Guid("b1000000-0000-0000-0000-000000000004"), new DateTime(2026, 7, 26, 21, 42, 39, 643, DateTimeKind.Utc).AddTicks(2792), true, "Comida Japonesa", null },
                    { new Guid("b1000000-0000-0000-0000-000000000005"), new DateTime(2026, 7, 26, 21, 42, 39, 643, DateTimeKind.Utc).AddTicks(2793), true, "Hamburgueria", null },
                    { new Guid("b1000000-0000-0000-0000-000000000006"), new DateTime(2026, 7, 26, 21, 42, 39, 643, DateTimeKind.Utc).AddTicks(2794), true, "Lanches", null },
                    { new Guid("b1000000-0000-0000-0000-000000000007"), new DateTime(2026, 7, 26, 21, 42, 39, 643, DateTimeKind.Utc).AddTicks(2795), true, "Pizzaria", null },
                    { new Guid("b1000000-0000-0000-0000-000000000008"), new DateTime(2026, 7, 26, 21, 42, 39, 643, DateTimeKind.Utc).AddTicks(2796), true, "Tapioca e crepes", null }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "CuisineTypes",
                keyColumn: "Id",
                keyValue: new Guid("b1000000-0000-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                table: "CuisineTypes",
                keyColumn: "Id",
                keyValue: new Guid("b1000000-0000-0000-0000-000000000002"));

            migrationBuilder.DeleteData(
                table: "CuisineTypes",
                keyColumn: "Id",
                keyValue: new Guid("b1000000-0000-0000-0000-000000000003"));

            migrationBuilder.DeleteData(
                table: "CuisineTypes",
                keyColumn: "Id",
                keyValue: new Guid("b1000000-0000-0000-0000-000000000004"));

            migrationBuilder.DeleteData(
                table: "CuisineTypes",
                keyColumn: "Id",
                keyValue: new Guid("b1000000-0000-0000-0000-000000000005"));

            migrationBuilder.DeleteData(
                table: "CuisineTypes",
                keyColumn: "Id",
                keyValue: new Guid("b1000000-0000-0000-0000-000000000006"));

            migrationBuilder.DeleteData(
                table: "CuisineTypes",
                keyColumn: "Id",
                keyValue: new Guid("b1000000-0000-0000-0000-000000000007"));

            migrationBuilder.DeleteData(
                table: "CuisineTypes",
                keyColumn: "Id",
                keyValue: new Guid("b1000000-0000-0000-0000-000000000008"));
        }
    }
}
