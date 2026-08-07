using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Urbeat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStoreBusinessHourShifts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsOpen",
                table: "StoreBusinessHours",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "StoreBusinessHourShift",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreBusinessHourId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreBusinessHourShift", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoreBusinessHourShift_StoreBusinessHours_StoreBusinessHourId",
                        column: x => x.StoreBusinessHourId,
                        principalTable: "StoreBusinessHours",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoreBusinessHourShift_StoreBusinessHourId",
                table: "StoreBusinessHourShift",
                column: "StoreBusinessHourId");

            // Migrate existing OpensAt/ClosesAt → one shift each
            migrationBuilder.Sql("""
                INSERT INTO "StoreBusinessHourShift" ("Id", "StoreBusinessHourId", "StartTime", "EndTime", "CreatedAtUtc")
                SELECT gen_random_uuid(), "Id", "OpensAt", "ClosesAt", now()
                FROM "StoreBusinessHours"
                WHERE "OpensAt" IS NOT NULL AND "ClosesAt" IS NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "ClosesAt",
                table: "StoreBusinessHours");

            migrationBuilder.DropColumn(
                name: "OpensAt",
                table: "StoreBusinessHours");

            migrationBuilder.UpdateData(
                table: "CuisineTypes",
                keyColumn: "Id",
                keyValue: new Guid("b1000000-0000-0000-0000-000000000001"),
                column: "CreatedAtUtc",
                value: new DateTime(2026, 7, 27, 16, 5, 40, 590, DateTimeKind.Utc).AddTicks(7004));

            migrationBuilder.UpdateData(
                table: "CuisineTypes",
                keyColumn: "Id",
                keyValue: new Guid("b1000000-0000-0000-0000-000000000002"),
                column: "CreatedAtUtc",
                value: new DateTime(2026, 7, 27, 16, 5, 40, 590, DateTimeKind.Utc).AddTicks(7998));

            migrationBuilder.UpdateData(
                table: "CuisineTypes",
                keyColumn: "Id",
                keyValue: new Guid("b1000000-0000-0000-0000-000000000003"),
                column: "CreatedAtUtc",
                value: new DateTime(2026, 7, 27, 16, 5, 40, 590, DateTimeKind.Utc).AddTicks(8001));

            migrationBuilder.UpdateData(
                table: "CuisineTypes",
                keyColumn: "Id",
                keyValue: new Guid("b1000000-0000-0000-0000-000000000004"),
                column: "CreatedAtUtc",
                value: new DateTime(2026, 7, 27, 16, 5, 40, 590, DateTimeKind.Utc).AddTicks(8003));

            migrationBuilder.UpdateData(
                table: "CuisineTypes",
                keyColumn: "Id",
                keyValue: new Guid("b1000000-0000-0000-0000-000000000005"),
                column: "CreatedAtUtc",
                value: new DateTime(2026, 7, 27, 16, 5, 40, 590, DateTimeKind.Utc).AddTicks(8005));

            migrationBuilder.UpdateData(
                table: "CuisineTypes",
                keyColumn: "Id",
                keyValue: new Guid("b1000000-0000-0000-0000-000000000006"),
                column: "CreatedAtUtc",
                value: new DateTime(2026, 7, 27, 16, 5, 40, 590, DateTimeKind.Utc).AddTicks(8006));

            migrationBuilder.UpdateData(
                table: "CuisineTypes",
                keyColumn: "Id",
                keyValue: new Guid("b1000000-0000-0000-0000-000000000007"),
                column: "CreatedAtUtc",
                value: new DateTime(2026, 7, 27, 16, 5, 40, 590, DateTimeKind.Utc).AddTicks(8008));

            migrationBuilder.UpdateData(
                table: "CuisineTypes",
                keyColumn: "Id",
                keyValue: new Guid("b1000000-0000-0000-0000-000000000008"),
                column: "CreatedAtUtc",
                value: new DateTime(2026, 7, 27, 16, 5, 40, 590, DateTimeKind.Utc).AddTicks(8009));

            migrationBuilder.CreateIndex(
                name: "IX_StoreBusinessHourShift_StoreBusinessHourId",
                table: "StoreBusinessHourShift",
                column: "StoreBusinessHourId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StoreBusinessHourShift");

            migrationBuilder.DropColumn(
                name: "IsOpen",
                table: "StoreBusinessHours");

            migrationBuilder.AddColumn<TimeOnly>(
                name: "ClosesAt",
                table: "StoreBusinessHours",
                type: "time",
                nullable: false,
                defaultValue: new TimeOnly(0, 0, 0));

            migrationBuilder.AddColumn<TimeOnly>(
                name: "OpensAt",
                table: "StoreBusinessHours",
                type: "time",
                nullable: false,
                defaultValue: new TimeOnly(0, 0, 0));

            migrationBuilder.UpdateData(
                table: "CuisineTypes",
                keyColumn: "Id",
                keyValue: new Guid("b1000000-0000-0000-0000-000000000001"),
                column: "CreatedAtUtc",
                value: new DateTime(2026, 7, 26, 21, 42, 39, 643, DateTimeKind.Utc).AddTicks(2478));

            migrationBuilder.UpdateData(
                table: "CuisineTypes",
                keyColumn: "Id",
                keyValue: new Guid("b1000000-0000-0000-0000-000000000002"),
                column: "CreatedAtUtc",
                value: new DateTime(2026, 7, 26, 21, 42, 39, 643, DateTimeKind.Utc).AddTicks(2789));

            migrationBuilder.UpdateData(
                table: "CuisineTypes",
                keyColumn: "Id",
                keyValue: new Guid("b1000000-0000-0000-0000-000000000003"),
                column: "CreatedAtUtc",
                value: new DateTime(2026, 7, 26, 21, 42, 39, 643, DateTimeKind.Utc).AddTicks(2791));

            migrationBuilder.UpdateData(
                table: "CuisineTypes",
                keyColumn: "Id",
                keyValue: new Guid("b1000000-0000-0000-0000-000000000004"),
                column: "CreatedAtUtc",
                value: new DateTime(2026, 7, 26, 21, 42, 39, 643, DateTimeKind.Utc).AddTicks(2792));

            migrationBuilder.UpdateData(
                table: "CuisineTypes",
                keyColumn: "Id",
                keyValue: new Guid("b1000000-0000-0000-0000-000000000005"),
                column: "CreatedAtUtc",
                value: new DateTime(2026, 7, 26, 21, 42, 39, 643, DateTimeKind.Utc).AddTicks(2793));

            migrationBuilder.UpdateData(
                table: "CuisineTypes",
                keyColumn: "Id",
                keyValue: new Guid("b1000000-0000-0000-0000-000000000006"),
                column: "CreatedAtUtc",
                value: new DateTime(2026, 7, 26, 21, 42, 39, 643, DateTimeKind.Utc).AddTicks(2794));

            migrationBuilder.UpdateData(
                table: "CuisineTypes",
                keyColumn: "Id",
                keyValue: new Guid("b1000000-0000-0000-0000-000000000007"),
                column: "CreatedAtUtc",
                value: new DateTime(2026, 7, 26, 21, 42, 39, 643, DateTimeKind.Utc).AddTicks(2795));

            migrationBuilder.UpdateData(
                table: "CuisineTypes",
                keyColumn: "Id",
                keyValue: new Guid("b1000000-0000-0000-0000-000000000008"),
                column: "CreatedAtUtc",
                value: new DateTime(2026, 7, 26, 21, 42, 39, 643, DateTimeKind.Utc).AddTicks(2796));
        }
    }
}
