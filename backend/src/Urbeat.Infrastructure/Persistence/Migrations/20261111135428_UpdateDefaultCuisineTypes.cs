using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Urbeat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UpdateDefaultCuisineTypes : Migration
    {
        // Normalized (accent/case-insensitive) keys of the 20 approved defaults.
        private const string ApprovedNormalizedList =
            "'acaiteria', 'batata recheada', 'cachorro-quente (hot dog)', 'cafeteria', 'caldos e sopas', " +
            "'comida japonesa', 'comida saudavel (fitness)', 'confeitaria', 'creperia', 'doceria', " +
            "'espetinhos e churrasco', 'hamburgueria', 'marmitaria', 'massas e lasanhas', 'padaria', " +
            "'pastelaria', 'pizzaria', 'salgaderia', 'sorveteria', 'tapiocaria'";

        // Normalized keys of the 15 defaults replaced by this migration. Used by Down only.
        private const string PreviousNormalizedList =
            "'acaiteria', 'cafeteria', 'churrascaria', 'comida arabe', 'comida japonesa', 'comida mexicana', " +
            "'doceria', 'hamburgueria', 'lanches', 'marmitaria', 'padaria', 'pastelaria', 'pizzaria', " +
            "'sucos e vitaminas', 'tapiocaria'";

        // (FixedId, Name, NormalizedName) for the 20 approved defaults. Names already seeded keep
        // their original ids (001, 004, 005, 007, 009, 012, 013, 014, 015 and 017); the ten new
        // categories use the stable ids 018-027. "Açaíteria" keeps the "acaiteria" key of the old
        // "Açaiteria" row, so it is corrected in place instead of duplicated.
        private const string ApprovedDefaultsWithIds =
            """
            ('b1000000-0000-0000-0000-000000000001'::uuid, 'Açaíteria', 'acaiteria'),
            ('b1000000-0000-0000-0000-000000000018'::uuid, 'Batata Recheada', 'batata recheada'),
            ('b1000000-0000-0000-0000-000000000019'::uuid, 'Cachorro-quente (Hot Dog)', 'cachorro-quente (hot dog)'),
            ('b1000000-0000-0000-0000-000000000009'::uuid, 'Cafeteria', 'cafeteria'),
            ('b1000000-0000-0000-0000-000000000020'::uuid, 'Caldos e Sopas', 'caldos e sopas'),
            ('b1000000-0000-0000-0000-000000000004'::uuid, 'Comida Japonesa', 'comida japonesa'),
            ('b1000000-0000-0000-0000-000000000021'::uuid, 'Comida Saudável (Fitness)', 'comida saudavel (fitness)'),
            ('b1000000-0000-0000-0000-000000000022'::uuid, 'Confeitaria', 'confeitaria'),
            ('b1000000-0000-0000-0000-000000000023'::uuid, 'Creperia', 'creperia'),
            ('b1000000-0000-0000-0000-000000000012'::uuid, 'Doceria', 'doceria'),
            ('b1000000-0000-0000-0000-000000000024'::uuid, 'Espetinhos e Churrasco', 'espetinhos e churrasco'),
            ('b1000000-0000-0000-0000-000000000005'::uuid, 'Hamburgueria', 'hamburgueria'),
            ('b1000000-0000-0000-0000-000000000013'::uuid, 'Marmitaria', 'marmitaria'),
            ('b1000000-0000-0000-0000-000000000025'::uuid, 'Massas e Lasanhas', 'massas e lasanhas'),
            ('b1000000-0000-0000-0000-000000000014'::uuid, 'Padaria', 'padaria'),
            ('b1000000-0000-0000-0000-000000000015'::uuid, 'Pastelaria', 'pastelaria'),
            ('b1000000-0000-0000-0000-000000000007'::uuid, 'Pizzaria', 'pizzaria'),
            ('b1000000-0000-0000-0000-000000000026'::uuid, 'Salgaderia', 'salgaderia'),
            ('b1000000-0000-0000-0000-000000000027'::uuid, 'Sorveteria', 'sorveteria'),
            ('b1000000-0000-0000-0000-000000000017'::uuid, 'Tapiocaria', 'tapiocaria')
            """;

        // (FixedId, Name, NormalizedName, CreatedAtUtc) of the previous defaults. Down restores
        // only the five rows this migration deleted; the others already exist.
        private const string PreviousDefaultsWithIds =
            """
            ('b1000000-0000-0000-0000-000000000003'::uuid, 'Comida Árabe', 'comida arabe', '2026-07-28 23:47:40+00'::timestamptz),
            ('b1000000-0000-0000-0000-000000000006'::uuid, 'Lanches', 'lanches', '2026-07-28 23:47:40+00'::timestamptz),
            ('b1000000-0000-0000-0000-000000000010'::uuid, 'Churrascaria', 'churrascaria', '2026-07-28 23:47:40+00'::timestamptz),
            ('b1000000-0000-0000-0000-000000000011'::uuid, 'Comida Mexicana', 'comida mexicana', '2026-07-28 23:47:40+00'::timestamptz),
            ('b1000000-0000-0000-0000-000000000016'::uuid, 'Sucos e Vitaminas', 'sucos e vitaminas', '2026-07-28 23:47:40+00'::timestamptz)
            """;

        // (Name, NormalizedName) of the fifteen previous defaults, used by Down to restore the
        // canonical names of rows that stayed in the global scope.
        private const string PreviousDefaults =
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

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Before removing any legacy global, copy every global outside the approved list that
            //    is still referenced by a store into a private category of that store and rewire
            //    Stores.CuisineTypeId. The original name/active value is preserved, so a store never
            //    loses (or silently changes) the category it already saved. When the store already
            //    owns a private category with the same normalized name, the exact legacy text is
            //    restored on that row (the normalized key already matches, so no unique index is
            //    violated and no duplicate row is created); an exact-name match is reused as is.
            migrationBuilder.Sql(
                $"""
                DROP TABLE IF EXISTS "_CuisineTypeLegacyCopyMap";
                CREATE TEMP TABLE "_CuisineTypeLegacyCopyMap" (
                    "LegacyId" uuid NOT NULL,
                    "StoreId" uuid NOT NULL,
                    "TargetId" uuid NOT NULL
                );

                INSERT INTO "_CuisineTypeLegacyCopyMap" ("LegacyId", "StoreId", "TargetId")
                SELECT DISTINCT ON (s."Id", c."NormalizedName")
                    c."Id",
                    s."Id",
                    COALESCE(p."Id", gen_random_uuid())
                FROM "CuisineTypes" AS c
                JOIN "Stores" AS s ON s."CuisineTypeId" = c."Id"
                LEFT JOIN LATERAL (
                    SELECT pc."Id"
                    FROM "CuisineTypes" AS pc
                    WHERE pc."StoreId" = s."Id"
                      AND pc."NormalizedName" = c."NormalizedName"
                    ORDER BY (pc."Name" = c."Name") DESC, pc."Id" ASC
                    LIMIT 1
                ) AS p ON TRUE
                WHERE c."StoreId" IS NULL
                  AND c."NormalizedName" NOT IN ({ApprovedNormalizedList})
                ORDER BY s."Id", c."NormalizedName", c."Id";

                -- A conflicting private category keeps its id/store link but receives the exact
                -- legacy text, so the value already saved by the store is never lost.
                UPDATE "CuisineTypes" AS pc
                SET "Name" = c."Name"
                FROM "_CuisineTypeLegacyCopyMap" AS m
                JOIN "CuisineTypes" AS c ON c."Id" = m."LegacyId"
                WHERE pc."Id" = m."TargetId"
                  AND pc."StoreId" IS NOT NULL
                  AND pc."Name" <> c."Name";

                INSERT INTO "CuisineTypes" ("Id", "Name", "NormalizedName", "IsActive", "IsDefault", "StoreId", "CreatedAtUtc")
                SELECT m."TargetId", c."Name", c."NormalizedName", c."IsActive", false, m."StoreId", now()
                FROM "_CuisineTypeLegacyCopyMap" AS m
                JOIN "CuisineTypes" AS c ON c."Id" = m."LegacyId"
                WHERE NOT EXISTS (
                    SELECT 1 FROM "CuisineTypes" AS existing WHERE existing."Id" = m."TargetId"
                );

                UPDATE "Stores" AS s
                SET "CuisineTypeId" = m."TargetId"
                FROM "_CuisineTypeLegacyCopyMap" AS m
                WHERE s."Id" = m."StoreId";

                DROP TABLE "_CuisineTypeLegacyCopyMap";
                """);

            // 2. Insert the approved defaults that are still missing. NOT EXISTS keeps the
            //    operation idempotent and never duplicates a normalized global key.
            migrationBuilder.Sql(
                $"""
                INSERT INTO "CuisineTypes" ("Id", "Name", "NormalizedName", "IsActive", "IsDefault", "StoreId", "CreatedAtUtc")
                SELECT v."Id", v."Name", v."NormalizedName", true, true, NULL, '2026-07-28 23:47:40+00'::timestamptz
                FROM (VALUES
                    {ApprovedDefaultsWithIds}
                ) AS v("Id", "Name", "NormalizedName")
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM "CuisineTypes" AS c
                    WHERE c."StoreId" IS NULL
                      AND c."NormalizedName" = v."NormalizedName"
                );
                """);

            // 3. Correct the canonical name of the approved globals that already existed.
            //    "Açaíteria" shares the "acaiteria" key with the legacy "Açaiteria" row, so this is
            //    the step that renames it in place.
            migrationBuilder.Sql(
                $"""
                UPDATE "CuisineTypes" AS c
                SET "Name" = v."Name",
                    "IsActive" = true
                FROM (VALUES
                    {ApprovedDefaultsWithIds}
                ) AS v("Id", "Name", "NormalizedName")
                WHERE c."StoreId" IS NULL
                  AND c."NormalizedName" = v."NormalizedName";
                """);

            // 4. Remove every remaining global outside the approved list. Referenced rows were
            //    rewired to private copies in step 1, so this only removes orphans.
            migrationBuilder.Sql(
                $"""
                DELETE FROM "CuisineTypes"
                WHERE "StoreId" IS NULL
                  AND "NormalizedName" NOT IN ({ApprovedNormalizedList});
                """);

            // 5. Only the 20 approved globals keep the protected flag; private categories are never
            //    defaults.
            migrationBuilder.Sql(
                $"""
                UPDATE "CuisineTypes"
                SET "IsDefault" = ("StoreId" IS NULL AND "NormalizedName" IN ({ApprovedNormalizedList}))
                WHERE "IsDefault" <> ("StoreId" IS NULL AND "NormalizedName" IN ({ApprovedNormalizedList}));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 1. Preserve any store that already references one of the new globals by copying it as
            //    a private category before the global row is removed. The same collision rule as Up
            //    applies: an existing private row with the same normalized name keeps its id/link
            //    and receives the exact legacy text.
            migrationBuilder.Sql(
                $"""
                DROP TABLE IF EXISTS "_CuisineTypeLegacyCopyMap";
                CREATE TEMP TABLE "_CuisineTypeLegacyCopyMap" (
                    "LegacyId" uuid NOT NULL,
                    "StoreId" uuid NOT NULL,
                    "TargetId" uuid NOT NULL
                );

                INSERT INTO "_CuisineTypeLegacyCopyMap" ("LegacyId", "StoreId", "TargetId")
                SELECT DISTINCT ON (s."Id", c."NormalizedName")
                    c."Id",
                    s."Id",
                    COALESCE(p."Id", gen_random_uuid())
                FROM "CuisineTypes" AS c
                JOIN "Stores" AS s ON s."CuisineTypeId" = c."Id"
                LEFT JOIN LATERAL (
                    SELECT pc."Id"
                    FROM "CuisineTypes" AS pc
                    WHERE pc."StoreId" = s."Id"
                      AND pc."NormalizedName" = c."NormalizedName"
                    ORDER BY (pc."Name" = c."Name") DESC, pc."Id" ASC
                    LIMIT 1
                ) AS p ON TRUE
                WHERE c."StoreId" IS NULL
                  AND c."NormalizedName" NOT IN ({PreviousNormalizedList})
                ORDER BY s."Id", c."NormalizedName", c."Id";

                -- A conflicting private category keeps its id/store link but receives the exact
                -- legacy text, so the value already saved by the store is never lost.
                UPDATE "CuisineTypes" AS pc
                SET "Name" = c."Name"
                FROM "_CuisineTypeLegacyCopyMap" AS m
                JOIN "CuisineTypes" AS c ON c."Id" = m."LegacyId"
                WHERE pc."Id" = m."TargetId"
                  AND pc."StoreId" IS NOT NULL
                  AND pc."Name" <> c."Name";

                INSERT INTO "CuisineTypes" ("Id", "Name", "NormalizedName", "IsActive", "IsDefault", "StoreId", "CreatedAtUtc")
                SELECT m."TargetId", c."Name", c."NormalizedName", c."IsActive", false, m."StoreId", now()
                FROM "_CuisineTypeLegacyCopyMap" AS m
                JOIN "CuisineTypes" AS c ON c."Id" = m."LegacyId"
                WHERE NOT EXISTS (
                    SELECT 1 FROM "CuisineTypes" AS existing WHERE existing."Id" = m."TargetId"
                );

                UPDATE "Stores" AS s
                SET "CuisineTypeId" = m."TargetId"
                FROM "_CuisineTypeLegacyCopyMap" AS m
                WHERE s."Id" = m."StoreId";

                DROP TABLE "_CuisineTypeLegacyCopyMap";
                """);

            // 2. Remove globals that did not exist before this migration (all referenced rows were
            //    copied in step 1).
            migrationBuilder.Sql(
                $"""
                DELETE FROM "CuisineTypes"
                WHERE "StoreId" IS NULL
                  AND "NormalizedName" NOT IN ({PreviousNormalizedList});
                """);

            // 3. Restore the previous defaults with their fixed ids, but only when neither the
            //    normalized key nor the id is already present in the global scope.
            migrationBuilder.Sql(
                $"""
                INSERT INTO "CuisineTypes" ("Id", "Name", "NormalizedName", "IsActive", "IsDefault", "StoreId", "CreatedAtUtc")
                SELECT v."Id", v."Name", v."NormalizedName", true, true, NULL, v."CreatedAtUtc"
                FROM (VALUES
                    {PreviousDefaultsWithIds}
                ) AS v("Id", "Name", "NormalizedName", "CreatedAtUtc")
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM "CuisineTypes" AS c
                    WHERE c."StoreId" IS NULL
                      AND c."NormalizedName" = v."NormalizedName"
                )
                  AND NOT EXISTS (
                    SELECT 1 FROM "CuisineTypes" AS c WHERE c."Id" = v."Id"
                );
                """);

            // 4. Restore the previous canonical names ("Açaíteria" goes back to "Açaiteria").
            migrationBuilder.Sql(
                $"""
                UPDATE "CuisineTypes" AS c
                SET "Name" = v."Name",
                    "IsActive" = true
                FROM (VALUES
                    {PreviousDefaults}
                ) AS v("Name", "NormalizedName")
                WHERE c."StoreId" IS NULL
                  AND c."NormalizedName" = v."NormalizedName";
                """);

            // 5. Restore the protected flag to the previous approved defaults only.
            migrationBuilder.Sql(
                $"""
                UPDATE "CuisineTypes"
                SET "IsDefault" = ("StoreId" IS NULL AND "NormalizedName" IN ({PreviousNormalizedList}))
                WHERE "IsDefault" <> ("StoreId" IS NULL AND "NormalizedName" IN ({PreviousNormalizedList}));
                """);
        }
    }
}
