using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Urbeat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCnefeGeographicBase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // IX_StoreDeliveryAreas_StoreId is intentionally kept: store and checkout queries still
            // resolve delivery areas by StoreId while transitional rows keep NeighborhoodId null.
            // Only the filtered unique index below is added for resolved identifiers.
            migrationBuilder.AlterColumn<string>(
                name: "Neighborhood",
                table: "StoreDeliveryAreas",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AddColumn<double>(
                name: "DistanceKm",
                table: "StoreDeliveryAreas",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DistanceMethod",
                table: "StoreDeliveryAreas",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "haversine");

            migrationBuilder.AddColumn<Guid>(
                name: "NeighborhoodId",
                table: "StoreDeliveryAreas",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Neighborhood",
                table: "DeliveryNeighborhoods",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AlterColumn<string>(
                name: "City",
                table: "DeliveryNeighborhoods",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AddColumn<DateTime>(
                name: "CoordinateCalculatedAtUtc",
                table: "DeliveryNeighborhoods",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CoordinateMethod",
                table: "DeliveryNeighborhoods",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CoordinatePointCount",
                table: "DeliveryNeighborhoods",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CoordinateSourceVersion",
                table: "DeliveryNeighborhoods",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CoordinateStatus",
                table: "DeliveryNeighborhoods",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "pending");

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "DeliveryNeighborhoods",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "legacy");

            migrationBuilder.AddColumn<string>(
                name: "LocalityType",
                table: "DeliveryNeighborhoods",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "RepresentativeLatitude",
                table: "DeliveryNeighborhoods",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "RepresentativeLongitude",
                table: "DeliveryNeighborhoods",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RepresentativeMethod",
                table: "DeliveryNeighborhoods",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CoordinateMethod",
                table: "Cities",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedName",
                table: "Cities",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "Cities",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "openstreetmap");

            migrationBuilder.AddColumn<string>(
                name: "SourceVersion",
                table: "Cities",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DeliveryStreets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CityId = table.Column<Guid>(type: "uuid", nullable: false),
                    NeighborhoodId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    StreetType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    StreetTitle = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Latitude = table.Column<double>(type: "double precision", nullable: true),
                    Longitude = table.Column<double>(type: "double precision", nullable: true),
                    CoordinateMethod = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    AddressPointCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    Source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SourceVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryStreets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeliveryStreets_Cities_CityId",
                        column: x => x.CityId,
                        principalTable: "Cities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DeliveryStreets_DeliveryNeighborhoods_NeighborhoodId",
                        column: x => x.NeighborhoodId,
                        principalTable: "DeliveryNeighborhoods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ImportRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceName = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SourceVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceChecksum = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Uf = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RowsRead = table.Column<long>(type: "bigint", nullable: false),
                    RowsStaged = table.Column<long>(type: "bigint", nullable: false),
                    RowsPublished = table.Column<long>(type: "bigint", nullable: false),
                    RowsRejected = table.Column<long>(type: "bigint", nullable: false),
                    RowsSkipped = table.Column<long>(type: "bigint", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Metadata = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    RollbackReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StoreDeliveryAreaMigrationIssues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreDeliveryAreaId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    Neighborhood = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    City = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Uf = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    Reason = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Details = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SourceVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreDeliveryAreaMigrationIssues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryStreetZipCodes",
                columns: table => new
                {
                    StreetId = table.Column<Guid>(type: "uuid", nullable: false),
                    ZipCode = table.Column<string>(type: "character(8)", fixedLength: true, maxLength: 8, nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    Source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SourceVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryStreetZipCodes", x => new { x.StreetId, x.ZipCode });
                    table.CheckConstraint("CK_DeliveryStreetZipCodes_ZipCode", "\"ZipCode\" ~ '^[0-9]{8}$'");
                    table.ForeignKey(
                        name: "FK_DeliveryStreetZipCodes_DeliveryStreets_StreetId",
                        column: x => x.StreetId,
                        principalTable: "DeliveryStreets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ImportDatasets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Uf = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ActivatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeactivatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportDatasets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportDatasets_ImportRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "ImportRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ImportErrors",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Uf = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    SourceTable = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    SourceRowId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Field = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    RawValue = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportErrors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportErrors_ImportRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "ImportRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoreDeliveryAreas_NeighborhoodId",
                table: "StoreDeliveryAreas",
                column: "NeighborhoodId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreDeliveryAreas_StoreId_NeighborhoodId",
                table: "StoreDeliveryAreas",
                columns: new[] { "StoreId", "NeighborhoodId" },
                unique: true,
                filter: "\"NeighborhoodId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNeighborhoods_CityId_IsActive",
                table: "DeliveryNeighborhoods",
                columns: new[] { "CityId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNeighborhoods_CityId_Kind",
                table: "DeliveryNeighborhoods",
                columns: new[] { "CityId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_Cities_Uf_NormalizedName",
                table: "Cities",
                columns: new[] { "Uf", "NormalizedName" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryStreets_CityId_NeighborhoodId_NormalizedName",
                table: "DeliveryStreets",
                columns: new[] { "CityId", "NeighborhoodId", "NormalizedName" },
                unique: true,
                filter: "\"NeighborhoodId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryStreets_NeighborhoodId",
                table: "DeliveryStreets",
                column: "NeighborhoodId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryStreets_NormalizedName",
                table: "DeliveryStreets",
                column: "NormalizedName");

            migrationBuilder.CreateIndex(
                name: "UX_DeliveryStreets_City_NullNeighborhood_NormalizedName",
                table: "DeliveryStreets",
                columns: new[] { "CityId", "NormalizedName" },
                unique: true,
                filter: "\"NeighborhoodId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryStreetZipCodes_ZipCode",
                table: "DeliveryStreetZipCodes",
                column: "ZipCode");

            migrationBuilder.CreateIndex(
                name: "UX_DeliveryStreetZipCodes_Street_Primary",
                table: "DeliveryStreetZipCodes",
                column: "StreetId",
                unique: true,
                filter: "\"IsPrimary\"");

            migrationBuilder.CreateIndex(
                name: "IX_ImportDatasets_RunId",
                table: "ImportDatasets",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportDatasets_SourceVersion_Uf",
                table: "ImportDatasets",
                columns: new[] { "SourceVersion", "Uf" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ImportDatasets_Active_Uf",
                table: "ImportDatasets",
                column: "Uf",
                unique: true,
                filter: "\"IsActive\"");

            migrationBuilder.CreateIndex(
                name: "IX_ImportErrors_RunId_Code",
                table: "ImportErrors",
                columns: new[] { "RunId", "Code" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportErrors_Uf",
                table: "ImportErrors",
                column: "Uf");

            migrationBuilder.CreateIndex(
                name: "IX_ImportRuns_SourceName_SourceVersion_SourceChecksum_Uf_Start~",
                table: "ImportRuns",
                columns: new[] { "SourceName", "SourceVersion", "SourceChecksum", "Uf", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportRuns_Status",
                table: "ImportRuns",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_StoreDeliveryAreaMigrationIssues_StoreDeliveryAreaId",
                table: "StoreDeliveryAreaMigrationIssues",
                column: "StoreDeliveryAreaId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoreDeliveryAreaMigrationIssues_StoreId",
                table: "StoreDeliveryAreaMigrationIssues",
                column: "StoreId");

            migrationBuilder.AddForeignKey(
                name: "FK_StoreDeliveryAreas_DeliveryNeighborhoods_NeighborhoodId",
                table: "StoreDeliveryAreas",
                column: "NeighborhoodId",
                principalTable: "DeliveryNeighborhoods",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // ── Transitional backfill (additive; nothing is deleted) ────────────────────────────
            // The normalized helpers below reproduce Urbeat.Domain.Geo.NeighborhoodName for the
            // Portuguese/Latin characters found in the existing data: lowercase, accents folded
            // with translate(), every other non-alphanumeric run collapsed to a single space.
            migrationBuilder.Sql("""
                WITH accent_map AS (
                    SELECT
                        'áàâãäåÁÀÂÃÄÅéèêëÉÈÊËíìîïÍÌÎÏóòôõöÓÒÔÕÖúùûüÚÙÛÜçÇñÑýÿÝ'::text AS source,
                        'aaaaaaAAAAAAeeeeEEEEiiiiIIIIoooooOOOOOuuuuUUUUcCnNyyY'::text AS target
                )
                UPDATE "Cities" c
                SET "NormalizedName" = lower(btrim(regexp_replace(
                        translate(c."Name", accent_map.source, accent_map.target),
                        '[^a-zA-Z0-9]+', ' ', 'g')))
                FROM accent_map
                WHERE c."NormalizedName" = '';
                """);

            migrationBuilder.Sql("""
                WITH accent_map AS (
                    SELECT
                        'áàâãäåÁÀÂÃÄÅéèêëÉÈÊËíìîïÍÌÎÏóòôõöÓÒÔÕÖúùûüÚÙÛÜçÇñÑýÿÝ'::text AS source,
                        'aaaaaaAAAAAAeeeeEEEEiiiiIIIIoooooOOOOOuuuuUUUUcCnNyyY'::text AS target
                ),
                candidates AS (
                    SELECT n."Id",
                           n."CityId",
                           lower(btrim(regexp_replace(
                               translate(n."Neighborhood", accent_map.source, accent_map.target),
                               '[^a-zA-Z0-9]+', ' ', 'g'))) AS new_name
                    FROM "DeliveryNeighborhoods" n, accent_map
                )
                UPDATE "DeliveryNeighborhoods" n
                SET "NormalizedName" = x.new_name
                FROM candidates x
                WHERE n."Id" = x."Id"
                  AND x.new_name <> ''
                  AND x.new_name <> n."NormalizedName"
                  AND NOT EXISTS (
                      SELECT 1
                      FROM candidates other
                      WHERE other."Id" <> x."Id"
                        AND other."CityId" IS NOT DISTINCT FROM x."CityId"
                        AND other.new_name = x.new_name
                  );
                """);

            // Legacy rows keep an explicit status: a row with a coordinate is treated as computed,
            // a row without one is manual (it is never filtered by radius). No coordinate is created.
            migrationBuilder.Sql("""
                UPDATE "DeliveryNeighborhoods"
                SET "CoordinateStatus" = CASE
                        WHEN "Latitude" IS NOT NULL AND "Longitude" IS NOT NULL THEN 'computed'
                        ELSE 'manual'
                    END
                WHERE "CoordinateStatus" = 'pending'
                  AND "CoordinateMethod" IS NULL;
                """);

            migrationBuilder.Sql("""
                UPDATE "DeliveryNeighborhoods"
                SET "Kind" = 'manual'
                WHERE "Kind" = 'legacy'
                  AND "Source" IS NULL
                  AND "Latitude" IS NULL
                  AND "Longitude" IS NULL;
                """);

            // StoreDeliveryAreas.NeighborhoodId backfill: store address city + canonical name.
            // Rows that cannot be resolved are kept untouched and reported below.
            // accent_map is a single-row CTE, so the CROSS JOIN inside the resolved CTE only brings
            // source/target into scope (no row duplication) for the city-name normalization.
            migrationBuilder.Sql("""
                WITH accent_map AS (
                    SELECT
                        'áàâãäåÁÀÂÃÄÅéèêëÉÈÊËíìîïÍÌÎÏóòôõöÓÒÔÕÖúùûüÚÙÛÜçÇñÑýÿÝ'::text AS source,
                        'aaaaaaAAAAAAeeeeEEEEiiiiIIIIoooooOOOOOuuuuUUUUcCnNyyY'::text AS target
                ),
                area_normalized AS (
                    SELECT a."Id" AS area_id,
                           a."StoreId" AS store_id,
                           lower(btrim(regexp_replace(
                               translate(a."Neighborhood", accent_map.source, accent_map.target),
                               '[^a-zA-Z0-9]+', ' ', 'g'))) AS normalized_name
                    FROM "StoreDeliveryAreas" a, accent_map
                    WHERE a."NeighborhoodId" IS NULL
                ),
                resolved AS (
                    SELECT an.area_id,
                           an.store_id,
                           n."Id" AS neighborhood_id,
                           CASE
                               WHEN sa."Latitude" IS NULL OR sa."Longitude" IS NULL
                                    OR n."Latitude" IS NULL OR n."Longitude" IS NULL THEN NULL
                               ELSE 6371.0 * 2.0 * asin(least(1.0, sqrt(
                                   power(sin(radians(n."Latitude" - sa."Latitude") / 2.0), 2)
                                   + cos(radians(sa."Latitude")) * cos(radians(n."Latitude"))
                                     * power(sin(radians(n."Longitude" - sa."Longitude") / 2.0), 2))))
                           END AS distance_km
                    FROM area_normalized an
                    CROSS JOIN accent_map
                    JOIN "StoreAddresses" sa ON sa."StoreId" = an.store_id
                    JOIN "Cities" c
                      ON lower(btrim(regexp_replace(
                             translate(c."Name", accent_map.source, accent_map.target),
                             '[^a-zA-Z0-9]+', ' ', 'g')))
                       = lower(btrim(regexp_replace(
                             translate(sa."City", accent_map.source, accent_map.target),
                             '[^a-zA-Z0-9]+', ' ', 'g')))
                     AND c."Uf" = upper(btrim(sa."State"))
                    JOIN "DeliveryNeighborhoods" n ON n."CityId" = c."Id"
                                                  AND n."NormalizedName" = an.normalized_name
                    WHERE an.normalized_name <> ''
                      AND NOT EXISTS (
                          SELECT 1 FROM "StoreDeliveryAreas" existing
                          WHERE existing."StoreId" = an.store_id
                            AND existing."NeighborhoodId" = n."Id"
                      )
                ),
                ranked AS (
                    SELECT resolved.*,
                           row_number() OVER (PARTITION BY store_id, neighborhood_id ORDER BY area_id) AS position
                    FROM resolved
                )
                UPDATE "StoreDeliveryAreas" a
                SET "NeighborhoodId" = r.neighborhood_id,
                    "DistanceKm" = r.distance_km,
                    "DistanceMethod" = 'haversine',
                    "UpdatedAtUtc" = now()
                FROM ranked r
                WHERE a."Id" = r.area_id
                  AND r.position = 1;
                """);

            migrationBuilder.Sql("""
                WITH accent_map AS (
                    SELECT
                        'áàâãäåÁÀÂÃÄÅéèêëÉÈÊËíìîïÍÌÎÏóòôõöÓÒÔÕÖúùûüÚÙÛÜçÇñÑýÿÝ'::text AS source,
                        'aaaaaaAAAAAAeeeeEEEEiiiiIIIIoooooOOOOOuuuuUUUUcCnNyyY'::text AS target
                )
                INSERT INTO "StoreDeliveryAreaMigrationIssues"
                    ("Id", "StoreDeliveryAreaId", "StoreId", "Neighborhood", "NormalizedName",
                     "City", "Uf", "Reason", "Details", "SourceVersion", "CreatedAtUtc")
                SELECT gen_random_uuid(),
                       a."Id",
                       a."StoreId",
                       a."Neighborhood",
                       lower(btrim(regexp_replace(
                           translate(a."Neighborhood", accent_map.source, accent_map.target),
                           '[^a-zA-Z0-9]+', ' ', 'g'))),
                       sa."City",
                       sa."State",
                       'unresolved_neighborhood',
                       'No active DeliveryNeighborhood matched the store address city + normalized name during the additive migration.',
                       'cnefe-migration-v1',
                       now()
                FROM "StoreDeliveryAreas" a
                CROSS JOIN accent_map
                LEFT JOIN "StoreAddresses" sa ON sa."StoreId" = a."StoreId"
                WHERE a."NeighborhoodId" IS NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM "StoreDeliveryAreaMigrationIssues" i
                      WHERE i."StoreDeliveryAreaId" = a."Id"
                  );
                """);
        }


        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StoreDeliveryAreas_DeliveryNeighborhoods_NeighborhoodId",
                table: "StoreDeliveryAreas");

            migrationBuilder.DropTable(
                name: "DeliveryStreetZipCodes");

            migrationBuilder.DropTable(
                name: "ImportDatasets");

            migrationBuilder.DropTable(
                name: "ImportErrors");

            migrationBuilder.DropTable(
                name: "StoreDeliveryAreaMigrationIssues");

            migrationBuilder.DropTable(
                name: "DeliveryStreets");

            migrationBuilder.DropTable(
                name: "ImportRuns");

            migrationBuilder.DropIndex(
                name: "IX_StoreDeliveryAreas_NeighborhoodId",
                table: "StoreDeliveryAreas");

            migrationBuilder.DropIndex(
                name: "IX_StoreDeliveryAreas_StoreId_NeighborhoodId",
                table: "StoreDeliveryAreas");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryNeighborhoods_CityId_IsActive",
                table: "DeliveryNeighborhoods");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryNeighborhoods_CityId_Kind",
                table: "DeliveryNeighborhoods");

            migrationBuilder.DropIndex(
                name: "IX_Cities_Uf_NormalizedName",
                table: "Cities");

            migrationBuilder.DropColumn(
                name: "DistanceKm",
                table: "StoreDeliveryAreas");

            migrationBuilder.DropColumn(
                name: "DistanceMethod",
                table: "StoreDeliveryAreas");

            migrationBuilder.DropColumn(
                name: "NeighborhoodId",
                table: "StoreDeliveryAreas");

            migrationBuilder.DropColumn(
                name: "CoordinateCalculatedAtUtc",
                table: "DeliveryNeighborhoods");

            migrationBuilder.DropColumn(
                name: "CoordinateMethod",
                table: "DeliveryNeighborhoods");

            migrationBuilder.DropColumn(
                name: "CoordinatePointCount",
                table: "DeliveryNeighborhoods");

            migrationBuilder.DropColumn(
                name: "CoordinateSourceVersion",
                table: "DeliveryNeighborhoods");

            migrationBuilder.DropColumn(
                name: "CoordinateStatus",
                table: "DeliveryNeighborhoods");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "DeliveryNeighborhoods");

            migrationBuilder.DropColumn(
                name: "LocalityType",
                table: "DeliveryNeighborhoods");

            migrationBuilder.DropColumn(
                name: "RepresentativeLatitude",
                table: "DeliveryNeighborhoods");

            migrationBuilder.DropColumn(
                name: "RepresentativeLongitude",
                table: "DeliveryNeighborhoods");

            migrationBuilder.DropColumn(
                name: "RepresentativeMethod",
                table: "DeliveryNeighborhoods");

            migrationBuilder.DropColumn(
                name: "CoordinateMethod",
                table: "Cities");

            migrationBuilder.DropColumn(
                name: "NormalizedName",
                table: "Cities");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "Cities");

            migrationBuilder.DropColumn(
                name: "SourceVersion",
                table: "Cities");

            migrationBuilder.AlterColumn<string>(
                name: "Neighborhood",
                table: "StoreDeliveryAreas",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150);

            migrationBuilder.AlterColumn<string>(
                name: "Neighborhood",
                table: "DeliveryNeighborhoods",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150);

            migrationBuilder.AlterColumn<string>(
                name: "City",
                table: "DeliveryNeighborhoods",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

        }
    }
}
