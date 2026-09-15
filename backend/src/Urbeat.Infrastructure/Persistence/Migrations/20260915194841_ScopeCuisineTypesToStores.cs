using System;
using System.Text;
using Microsoft.EntityFrameworkCore.Migrations;
using Urbeat.Domain.Entities;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Urbeat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScopeCuisineTypesToStores : Migration
    {
        // Normalized (accent/case-insensitive) names of the 15 approved protected defaults.
        private const string ApprovedNormalizedList =
            "'acaiteria', 'cafeteria', 'churrascaria', 'comida arabe', 'comida japonesa', 'comida mexicana', " +
            "'doceria', 'hamburgueria', 'lanches', 'marmitaria', 'padaria', 'pastelaria', 'pizzaria', " +
            "'sucos e vitaminas', 'tapiocaria'";

        // (FixedId, Name, NormalizedName) for the 15 approved defaults. Fixed ids match the original seed.
        private const string ApprovedDefaultsWithIds =
            """
            ('b1000000-0000-0000-0000-000000000001'::uuid, 'Açaiteria', 'acaiteria'),
            ('b1000000-0000-0000-0000-000000000009'::uuid, 'Cafeteria', 'cafeteria'),
            ('b1000000-0000-0000-0000-000000000010'::uuid, 'Churrascaria', 'churrascaria'),
            ('b1000000-0000-0000-0000-000000000003'::uuid, 'Comida Árabe', 'comida arabe'),
            ('b1000000-0000-0000-0000-000000000004'::uuid, 'Comida Japonesa', 'comida japonesa'),
            ('b1000000-0000-0000-0000-000000000011'::uuid, 'Comida Mexicana', 'comida mexicana'),
            ('b1000000-0000-0000-0000-000000000012'::uuid, 'Doceria', 'doceria'),
            ('b1000000-0000-0000-0000-000000000005'::uuid, 'Hamburgueria', 'hamburgueria'),
            ('b1000000-0000-0000-0000-000000000006'::uuid, 'Lanches', 'lanches'),
            ('b1000000-0000-0000-0000-000000000013'::uuid, 'Marmitaria', 'marmitaria'),
            ('b1000000-0000-0000-0000-000000000014'::uuid, 'Padaria', 'padaria'),
            ('b1000000-0000-0000-0000-000000000015'::uuid, 'Pastelaria', 'pastelaria'),
            ('b1000000-0000-0000-0000-000000000007'::uuid, 'Pizzaria', 'pizzaria'),
            ('b1000000-0000-0000-0000-000000000016'::uuid, 'Sucos e Vitaminas', 'sucos e vitaminas'),
            ('b1000000-0000-0000-0000-000000000017'::uuid, 'Tapiocaria', 'tapiocaria')
            """;

        // (Name, NormalizedName) used when renaming/inserting the canonical default rows.
        private const string ApprovedDefaults =
            """
            ('Açaiteria', 'acaiteria'),
            ('Cafeteria', 'cafeteria'),
            ('Churrascaria', 'churrascaria'),
            ('Comida Árabe', 'comida arabe'),
            ('Comida Japonesa', 'comida japonesa'),
            ('Comida Mexicana', 'comida mexicana'),
            ('Doceria', 'doceria'),
            ('Hamburgueria', 'hamburgueria'),
            ('Lanches', 'lanches'),
            ('Marmitaria', 'marmitaria'),
            ('Padaria', 'padaria'),
            ('Pastelaria', 'pastelaria'),
            ('Pizzaria', 'pizzaria'),
            ('Sucos e Vitaminas', 'sucos e vitaminas'),
            ('Tapiocaria', 'tapiocaria')
            """;

        // Mirrors CuisineType.NormalizeName for Latin text without depending on a database extension:
        // lower + trim, fold every precomposed Latin letter that .NET decomposes, then drop the
        // combining diacritical marks (U+0300-U+036F) so decomposed input is normalized too.
        // The map is generated from the domain function itself to stay consistent with the seeder.
        private static readonly string NameNormalizationSql = BuildNameNormalizationSql();

        private static string BuildNameNormalizationSql()
        {
            var source = new StringBuilder();
            var target = new StringBuilder();

            for (var code = 0x00C0; code <= 0x024F; code++)
            {
                var ch = (char)code;
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch)
                    == System.Globalization.UnicodeCategory.UppercaseLetter)
                {
                    // lower() already lowercases uppercase precomposed letters before folding.
                    continue;
                }

                var normalized = CuisineType.NormalizeName(ch.ToString());
                if (normalized.Length == 1 && normalized[0] != ch)
                {
                    source.Append(ch);
                    target.Append(normalized[0]);
                }
            }

            var combiningMarks = new StringBuilder();
            for (var code = 0x0300; code <= 0x036F; code++)
            {
                combiningMarks.Append((char)code);
            }

            return $"translate(translate(lower(btrim(\"Name\")), '{source}', '{target}'), '{combiningMarks}', '')";
        }

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CuisineTypes_Name",
                table: "CuisineTypes");

            migrationBuilder.AddColumn<bool>(
                name: "IsDefault",
                table: "CuisineTypes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedName",
                table: "CuisineTypes",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "StoreId",
                table: "CuisineTypes",
                type: "uuid",
                nullable: true);

            // 1. Backfill the canonical key with the same strategy used by the seeder/domain.
            migrationBuilder.Sql(
                $"""
                UPDATE "CuisineTypes"
                SET "NormalizedName" = {NameNormalizationSql};
                """);

            // 2. Copy/rewire every referenced global category that is NOT one of the approved
            //    defaults BEFORE any promotion. This preserves the original value per store and
            //    never silently maps a legacy category (including old defaults outside the list)
            //    to a protected default. Each store gets its own private copy.
            migrationBuilder.Sql(
                $"""
                DROP TABLE IF EXISTS "_CuisineTypeLegacyMap";
                CREATE TEMP TABLE "_CuisineTypeLegacyMap" (
                    "LegacyId" uuid NOT NULL,
                    "StoreId" uuid NOT NULL,
                    "NewId" uuid NOT NULL
                );

                INSERT INTO "_CuisineTypeLegacyMap" ("LegacyId", "StoreId", "NewId")
                SELECT DISTINCT ON (s."Id", c."NormalizedName")
                    c."Id",
                    s."Id",
                    gen_random_uuid()
                FROM "CuisineTypes" AS c
                JOIN "Stores" AS s ON s."CuisineTypeId" = c."Id"
                WHERE c."StoreId" IS NULL
                  AND c."NormalizedName" NOT IN ({ApprovedNormalizedList})
                ORDER BY s."Id", c."NormalizedName", c."Id";

                INSERT INTO "CuisineTypes" ("Id", "Name", "NormalizedName", "IsActive", "IsDefault", "StoreId", "CreatedAtUtc")
                SELECT m."NewId", c."Name", c."NormalizedName", c."IsActive", false, m."StoreId", now()
                FROM "_CuisineTypeLegacyMap" AS m
                JOIN "CuisineTypes" AS c ON c."Id" = m."LegacyId";

                UPDATE "Stores" AS s
                SET "CuisineTypeId" = m."NewId"
                FROM "_CuisineTypeLegacyMap" AS m
                WHERE s."Id" = m."StoreId";

                DROP TABLE "_CuisineTypeLegacyMap";
                """);

            // 3. Consolidate and promote the 15 approved defaults. Approved duplicates (names that
            //    differ only by case/accent) are merged into one canonical global row; stores that
            //    referenced a duplicate are rewired to the canonical row before it is removed.
            migrationBuilder.Sql(
                $"""
                DROP TABLE IF EXISTS "_CuisineTypeDefaultMap";
                CREATE TEMP TABLE "_CuisineTypeDefaultMap" (
                    "NormalizedName" text NOT NULL,
                    "CanonicalId" uuid NOT NULL
                );

                INSERT INTO "_CuisineTypeDefaultMap" ("NormalizedName", "CanonicalId")
                SELECT d."NormalizedName",
                       COALESCE(
                           (SELECT c."Id"
                            FROM "CuisineTypes" AS c
                            WHERE c."StoreId" IS NULL
                              AND c."NormalizedName" = d."NormalizedName"
                            ORDER BY c."IsDefault" DESC, c."CreatedAtUtc" ASC, c."Id" ASC
                            LIMIT 1),
                           d."Id")
                FROM (VALUES
                    {ApprovedDefaultsWithIds}
                ) AS d("Id", "Name", "NormalizedName");

                INSERT INTO "CuisineTypes" ("Id", "Name", "NormalizedName", "IsActive", "IsDefault", "StoreId", "CreatedAtUtc")
                SELECT m."CanonicalId", d."Name", d."NormalizedName", true, true, NULL, now()
                FROM "_CuisineTypeDefaultMap" AS m
                JOIN (VALUES
                    {ApprovedDefaults}
                ) AS d("Name", "NormalizedName") ON d."NormalizedName" = m."NormalizedName"
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM "CuisineTypes" AS c
                    WHERE c."StoreId" IS NULL
                      AND c."NormalizedName" = d."NormalizedName"
                );

                UPDATE "Stores" AS s
                SET "CuisineTypeId" = m."CanonicalId"
                FROM "CuisineTypes" AS c
                JOIN "_CuisineTypeDefaultMap" AS m ON m."NormalizedName" = c."NormalizedName"
                WHERE s."CuisineTypeId" = c."Id"
                  AND c."StoreId" IS NULL
                  AND c."Id" <> m."CanonicalId";

                DELETE FROM "CuisineTypes" AS c
                USING "_CuisineTypeDefaultMap" AS m
                WHERE c."StoreId" IS NULL
                  AND c."NormalizedName" = m."NormalizedName"
                  AND c."Id" <> m."CanonicalId";

                UPDATE "CuisineTypes" AS c
                SET "Name" = d."Name",
                    "NormalizedName" = d."NormalizedName",
                    "IsDefault" = true,
                    "IsActive" = true,
                    "StoreId" = NULL
                FROM "_CuisineTypeDefaultMap" AS m
                JOIN (VALUES
                    {ApprovedDefaults}
                ) AS d("Name", "NormalizedName") ON d."NormalizedName" = m."NormalizedName"
                WHERE c."Id" = m."CanonicalId";

                DROP TABLE "_CuisineTypeDefaultMap";
                """);

            // 4. Remove every remaining global category that is not one of the 15 approved defaults.
            //    This includes legacy rows that were flagged IsDefault before this migration; they
            //    are now unreferenced because step 2 rewired their stores to private copies.
            migrationBuilder.Sql(
                $"""
                DELETE FROM "CuisineTypes"
                WHERE "StoreId" IS NULL
                  AND "NormalizedName" NOT IN ({ApprovedNormalizedList});
                """);

            migrationBuilder.CreateIndex(
                name: "IX_CuisineTypes_NormalizedName",
                table: "CuisineTypes",
                column: "NormalizedName",
                unique: true,
                filter: "\"StoreId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CuisineTypes_StoreId_NormalizedName",
                table: "CuisineTypes",
                columns: new[] { "StoreId", "NormalizedName" },
                unique: true,
                filter: "\"StoreId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_CuisineTypes_Stores_StoreId",
                table: "CuisineTypes",
                column: "StoreId",
                principalTable: "Stores",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CuisineTypes_Stores_StoreId",
                table: "CuisineTypes");

            migrationBuilder.DropIndex(
                name: "IX_CuisineTypes_NormalizedName",
                table: "CuisineTypes");

            migrationBuilder.DropIndex(
                name: "IX_CuisineTypes_StoreId_NormalizedName",
                table: "CuisineTypes");

            // Regroup every row by its normalized name instead of deleting private categories:
            // stores are rewired to one canonical row per name, then only the duplicate rows are
            // removed. This preserves each store's category value (same name) and avoids dropping
            // data that the old schema (unique index on Name) could not represent twice.
            migrationBuilder.Sql(
                """
                DROP TABLE IF EXISTS "_CuisineTypeDownMap";
                CREATE TEMP TABLE "_CuisineTypeDownMap" (
                    "NormalizedName" text NOT NULL,
                    "CanonicalId" uuid NOT NULL
                );

                INSERT INTO "_CuisineTypeDownMap" ("NormalizedName", "CanonicalId")
                SELECT c."NormalizedName",
                       (SELECT c2."Id"
                        FROM "CuisineTypes" AS c2
                        WHERE c2."NormalizedName" = c."NormalizedName"
                        ORDER BY (c2."StoreId" IS NULL) DESC, c2."CreatedAtUtc" ASC, c2."Id" ASC
                        LIMIT 1)
                FROM "CuisineTypes" AS c
                GROUP BY c."NormalizedName";

                UPDATE "Stores" AS s
                SET "CuisineTypeId" = m."CanonicalId"
                FROM "CuisineTypes" AS c
                JOIN "_CuisineTypeDownMap" AS m ON m."NormalizedName" = c."NormalizedName"
                WHERE s."CuisineTypeId" = c."Id"
                  AND c."Id" <> m."CanonicalId";

                DELETE FROM "CuisineTypes" AS c
                USING "_CuisineTypeDownMap" AS m
                WHERE c."NormalizedName" = m."NormalizedName"
                  AND c."Id" <> m."CanonicalId";

                DROP TABLE "_CuisineTypeDownMap";
                """);

            migrationBuilder.DropColumn(
                name: "IsDefault",
                table: "CuisineTypes");

            migrationBuilder.DropColumn(
                name: "NormalizedName",
                table: "CuisineTypes");

            migrationBuilder.DropColumn(
                name: "StoreId",
                table: "CuisineTypes");

            // Restore the two original seeded categories removed by Up, guarded so a re-run cannot
            // duplicate them and existing data is never overwritten.
            migrationBuilder.Sql(
                """
                INSERT INTO "CuisineTypes" ("Id", "Name", "IsActive", "CreatedAtUtc")
                SELECT v."Id", v."Name", true, now()
                FROM (VALUES
                    ('b1000000-0000-0000-0000-000000000002'::uuid, 'Cachorro Quente'),
                    ('b1000000-0000-0000-0000-000000000008'::uuid, 'Tapioca e crepes')
                ) AS v("Id", "Name")
                WHERE NOT EXISTS (SELECT 1 FROM "CuisineTypes" AS c WHERE c."Name" = v."Name")
                  AND NOT EXISTS (SELECT 1 FROM "CuisineTypes" AS c WHERE c."Id" = v."Id");
                """);

            migrationBuilder.CreateIndex(
                name: "IX_CuisineTypes_Name",
                table: "CuisineTypes",
                column: "Name",
                unique: true);
        }
    }
}
