using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Urbeat.Infrastructure.Persistence.Migrations;

namespace Urbeat.UnitTests.Infrastructure.Persistence;

public sealed class CuisineTypeMigrationTests
{
    private readonly ScopeCuisineTypesToStores _migration = new();

    private IReadOnlyList<SqlOperation> UpSqlOperations => _migration.UpOperations
        .OfType<SqlOperation>()
        .ToList();

    private string UpSql => string.Join(
        "\n",
        UpSqlOperations.Select(operation => operation.Sql));

    private IReadOnlyList<SqlOperation> DownSqlOperations => _migration.DownOperations
        .OfType<SqlOperation>()
        .ToList();

    private string DownSql => string.Join(
        "\n",
        DownSqlOperations.Select(operation => operation.Sql));

    [Fact]
    public void Up_ShouldAddScopeColumns_ToCuisineTypes()
    {
        var up = _migration.UpOperations;

        var isDefault = up.OfType<AddColumnOperation>()
            .Single(x => x.Table == "CuisineTypes" && x.Name == "IsDefault");
        isDefault.ClrType.Should().Be(typeof(bool));
        isDefault.IsNullable.Should().BeFalse();

        var storeId = up.OfType<AddColumnOperation>()
            .Single(x => x.Table == "CuisineTypes" && x.Name == "StoreId");
        storeId.ClrType.Should().Be(typeof(Guid));
        storeId.IsNullable.Should().BeTrue();

        var normalizedName = up.OfType<AddColumnOperation>()
            .Single(x => x.Table == "CuisineTypes" && x.Name == "NormalizedName");
        normalizedName.ClrType.Should().Be(typeof(string));
        normalizedName.IsNullable.Should().BeFalse();
        normalizedName.MaxLength.Should().Be(80);
    }

    [Fact]
    public void Up_ShouldPreserveStoreCuisineTypeId()
    {
        _migration.UpOperations.OfType<DropColumnOperation>()
            .Should().NotContain(x => x.Table == "Stores" && x.Name == "CuisineTypeId");
        _migration.DownOperations.OfType<DropColumnOperation>()
            .Should().NotContain(x => x.Table == "Stores" && x.Name == "CuisineTypeId");
    }

    [Fact]
    public void Up_ShouldCreateScopedUniqueIndexes_OnNormalizedName()
    {
        var up = _migration.UpOperations;

        up.OfType<CreateIndexOperation>().Should().Contain(x =>
            x.Table == "CuisineTypes"
            && x.IsUnique
            && x.Columns.SequenceEqual(new[] { "StoreId", "NormalizedName" }));

        up.OfType<CreateIndexOperation>().Should().Contain(x =>
            x.Table == "CuisineTypes"
            && x.IsUnique
            && x.Columns.SequenceEqual(new[] { "NormalizedName" })
            && x.Filter != null
            && x.Filter.Contains("StoreId"));
    }

    [Fact]
    public void Up_ShouldAddRestrictForeignKey_ToStores()
    {
        var foreignKey = _migration.UpOperations.OfType<AddForeignKeyOperation>()
            .Single(x => x.Table == "CuisineTypes" && x.Columns.SequenceEqual(new[] { "StoreId" }));

        foreignKey.PrincipalTable.Should().Be("Stores");
        foreignKey.OnDelete.Should().Be(ReferentialAction.Restrict);
    }

    [Fact]
    public void Up_ShouldNormalizeNames_UsingDomainConsistentStrategy()
    {
        // The backfill must trim/lower, fold precomposed Latin letters and drop combining marks
        // instead of a partial fixed translation table.
        UpSql.Should().Contain("translate");
        UpSql.Should().Contain("lower(btrim(\"Name\"))");
        UpSql.Should().Contain("\"NormalizedName\"");
    }

    [Fact]
    public void Up_ShouldCopyAndRewireUnapprovedReferencedGlobals_BeforePromotingDefaults()
    {
        var copyIndex = -1;
        var promoteIndex = -1;

        for (var i = 0; i < UpSqlOperations.Count; i++)
        {
            var sql = UpSqlOperations[i].Sql;

            if (sql.Contains("gen_random_uuid()", StringComparison.Ordinal))
            {
                copyIndex = i;
            }

            if (promoteIndex < 0 && sql.Contains("\"IsDefault\" = true", StringComparison.Ordinal))
            {
                promoteIndex = i;
            }
        }

        copyIndex.Should().BeGreaterThanOrEqualTo(0);
        promoteIndex.Should().BeGreaterThanOrEqualTo(0);
        copyIndex.Should().BeLessThan(promoteIndex, "legacy copies must happen before defaults are promoted");
    }

    [Fact]
    public void Up_ShouldDeleteEveryUnapprovedGlobal_RegardlessOfDefaultFlag()
    {
        var deleteSql = UpSqlOperations
            .Select(x => x.Sql)
            .Single(x => x.Contains("DELETE FROM \"CuisineTypes\"", StringComparison.Ordinal)
                && x.Contains("NOT IN", StringComparison.Ordinal));

        deleteSql.Should().Contain("NOT IN");
        deleteSql.Should().Contain("acaiteria");
        deleteSql.Should().Contain("tapiocaria");
        deleteSql.Should().NotContain("\"IsDefault\" = false");
    }

    [Fact]
    public void Up_ShouldNotTouchCategoriesFromOtherDomains()
    {
        var up = _migration.UpOperations;

        up.OfType<SqlOperation>()
            .Should().NotContain(x => x.Sql.Contains("\"ProductCategories\"", StringComparison.Ordinal));
        up.OfType<DropColumnOperation>()
            .Should().NotContain(x => x.Table == "ProductCategories");
        up.OfType<AddColumnOperation>()
            .Should().NotContain(x => x.Table == "ProductCategories");
        up.OfType<DropTableOperation>()
            .Should().NotContain(x => x.Name == "ProductCategories");
    }

    [Fact]
    public void Down_ShouldRegroupStoreReferences_BeforeRemovingDuplicates()
    {
        DownSql.Should().Contain("_CuisineTypeDownMap");
        DownSql.Should().Contain("UPDATE \"Stores\"");
        DownSql.Should().Contain("DELETE FROM \"CuisineTypes\"");
        DownSql.Should().Contain("GROUP BY c.\"Name\"");
    }

    [Fact]
    public void Down_ShouldCollapseOnlyExactDuplicateNames_PreservingCaseAndAccentVariants()
    {
        // The old schema has a unique index on the exact Name, so two private categories that
        // differ only by case/accent can coexist. Merging by NormalizedName would delete one of
        // them and silently rewrite a store to a different original name.
        DownSql.Should().Contain("GROUP BY c.\"Name\"");
        DownSql.Should().NotContain("GROUP BY c.\"NormalizedName\"");
        DownSql.Should().Contain("m.\"Name\" = c.\"Name\"");
        DownSql.Should().NotContain("m.\"NormalizedName\"");
    }

    [Fact]
    public void Down_ShouldNotDeletePrivateCategoriesUnconditionally()
    {
        DownSql.Should().NotContain("DELETE FROM \"CuisineTypes\" WHERE \"StoreId\" IS NOT NULL");
        DownSql.Should().NotContain("SET \"CuisineTypeId\" = NULL");
    }

    [Fact]
    public void Down_ShouldNotSilentlyDeleteSeededDefaults()
    {
        _migration.DownOperations.OfType<DeleteDataOperation>().Should().BeEmpty();
    }

    [Fact]
    public void Down_ShouldRestoreOriginalSeedRows_GuardedByIdempotencyCheck()
    {
        DownSql.Should().Contain("Cachorro Quente");
        DownSql.Should().Contain("Tapioca e crepes");
        DownSql.Should().Contain("NOT EXISTS");

        var restoreSql = DownSqlOperations
            .Single(x => x.Sql.Contains("Cachorro Quente", StringComparison.Ordinal)
                && x.Sql.Contains("Tapioca e crepes", StringComparison.Ordinal)).Sql;

        // Fixed ids keep the restoration deterministic and non-duplicating.
        restoreSql.Should().Contain("'b1000000-0000-0000-0000-000000000002'::uuid");
        restoreSql.Should().Contain("'b1000000-0000-0000-0000-000000000008'::uuid");
        // Absence is checked against the global scope (StoreId IS NULL), not any scope.
        restoreSql.Should().Contain("c.\"StoreId\" IS NULL");
    }

    [Fact]
    public void Down_ShouldRestoreLegacySeeds_BeforeCollapsingDuplicates()
    {
        var restoreIndex = DownSqlOperations
            .Select((operation, index) => (operation, index))
            .First(x => x.operation.Sql.Contains("Cachorro Quente", StringComparison.Ordinal)
                && x.operation.Sql.Contains("Tapioca e crepes", StringComparison.Ordinal))
            .index;

        var collapseIndex = DownSqlOperations
            .Select((operation, index) => (operation, index))
            .First(x => x.operation.Sql.Contains("_CuisineTypeDownMap", StringComparison.Ordinal))
            .index;

        restoreIndex.Should().BeLessThan(
            collapseIndex,
            "the legacy seeds must exist before the exact-name merge so a global seed wins it");
    }

    [Fact]
    public void Down_ShouldDropScopeColumnsAndIndexes()
    {
        var down = _migration.DownOperations;

        down.OfType<DropColumnOperation>()
            .Should().Contain(x => x.Table == "CuisineTypes" && x.Name == "StoreId");
        down.OfType<DropColumnOperation>()
            .Should().Contain(x => x.Table == "CuisineTypes" && x.Name == "IsDefault");
        down.OfType<DropColumnOperation>()
            .Should().Contain(x => x.Table == "CuisineTypes" && x.Name == "NormalizedName");

        down.OfType<DropIndexOperation>().Should().Contain(x => x.Name == "IX_CuisineTypes_StoreId_NormalizedName");
        down.OfType<DropIndexOperation>().Should().Contain(x => x.Name == "IX_CuisineTypes_NormalizedName");
    }
}

public sealed class UpdateDefaultCuisineTypesMigrationTests
{
    private static readonly string[] NewDefaultNames =
    [
        "Batata Recheada",
        "Cachorro-quente (Hot Dog)",
        "Caldos e Sopas",
        "Comida Saudável (Fitness)",
        "Confeitaria",
        "Creperia",
        "Espetinhos e Churrasco",
        "Massas e Lasanhas",
        "Salgaderia",
        "Sorveteria"
    ];

    private static readonly string[] RemovedDefaultNames =
    [
        "Comida Árabe",
        "Churrascaria",
        "Comida Mexicana",
        "Lanches",
        "Sucos e Vitaminas"
    ];

    private readonly UpdateDefaultCuisineTypes _migration = new();

    private IReadOnlyList<SqlOperation> UpSqlOperations => _migration.UpOperations
        .OfType<SqlOperation>()
        .ToList();

    private string UpSql => string.Join(
        "\n",
        UpSqlOperations.Select(operation => operation.Sql));

    private IReadOnlyList<SqlOperation> DownSqlOperations => _migration.DownOperations
        .OfType<SqlOperation>()
        .ToList();

    private string DownSql => string.Join(
        "\n",
        DownSqlOperations.Select(operation => operation.Sql));

    private SqlOperation UpInsertDefaultsOperation => UpSqlOperations
        .Single(x => x.Sql.Contains("INSERT INTO \"CuisineTypes\"", StringComparison.Ordinal)
            && x.Sql.Contains("Açaíteria", StringComparison.Ordinal));

    private SqlOperation DownRestoreDefaultsOperation => DownSqlOperations
        .Single(x => x.Sql.Contains("INSERT INTO \"CuisineTypes\"", StringComparison.Ordinal)
            && x.Sql.Contains("Sucos e Vitaminas", StringComparison.Ordinal));

    [Fact]
    public void Up_ShouldNotBlindlyDeleteLegacyRows()
    {
        _migration.UpOperations.OfType<DeleteDataOperation>().Should().BeEmpty();
        _migration.UpOperations.OfType<UpdateDataOperation>().Should().BeEmpty();
    }

    [Fact]
    public void Up_ShouldInsertEveryNewDefault_GuardedByNotExists()
    {
        var insertSql = UpInsertDefaultsOperation.Sql;

        foreach (var name in NewDefaultNames)
        {
            insertSql.Should().Contain($"'{name}'");
        }

        insertSql.Should().Contain("NOT EXISTS");
        insertSql.Should().Contain("\"StoreId\" IS NULL");
        insertSql.Should().Contain("'b1000000-0000-0000-0000-000000000018'::uuid");
        insertSql.Should().Contain("'b1000000-0000-0000-0000-000000000027'::uuid");
    }

    [Fact]
    public void Up_ShouldReuseStableIdsForRetainedDefaults()
    {
        var insertSql = UpInsertDefaultsOperation.Sql;

        insertSql.Should().Contain("'b1000000-0000-0000-0000-000000000001'::uuid");
        insertSql.Should().Contain("'b1000000-0000-0000-0000-000000000004'::uuid");
        insertSql.Should().Contain("'b1000000-0000-0000-0000-000000000005'::uuid");
        insertSql.Should().Contain("'b1000000-0000-0000-0000-000000000007'::uuid");
        insertSql.Should().Contain("'b1000000-0000-0000-0000-000000000009'::uuid");
        insertSql.Should().Contain("'b1000000-0000-0000-0000-000000000012'::uuid");
        insertSql.Should().Contain("'b1000000-0000-0000-0000-000000000013'::uuid");
        insertSql.Should().Contain("'b1000000-0000-0000-0000-000000000014'::uuid");
        insertSql.Should().Contain("'b1000000-0000-0000-0000-000000000015'::uuid");
        insertSql.Should().Contain("'b1000000-0000-0000-0000-000000000017'::uuid");
    }

    [Fact]
    public void Up_ShouldRenameLegacyAcaiteria_InsteadOfDuplicatingIt()
    {
        var renameSql = UpSqlOperations
            .Single(x => x.Sql.Contains("SET \"Name\" = v.\"Name\"", StringComparison.Ordinal))
            .Sql;

        renameSql.Should().Contain("'Açaíteria'");
        renameSql.Should().Contain("'acaiteria'");
        renameSql.Should().Contain("\"StoreId\" IS NULL");
    }

    [Fact]
    public void Up_ShouldCopyAndRewireReferencedLegacyGlobals_BeforeDeletingGlobals()
    {
        var copyIndex = UpSqlOperations
            .Select((operation, index) => (operation, index))
            .First(x => x.operation.Sql.Contains("_CuisineTypeLegacyCopyMap", StringComparison.Ordinal))
            .index;

        var deleteIndex = UpSqlOperations
            .Select((operation, index) => (operation, index))
            .First(x => x.operation.Sql.Contains("DELETE FROM \"CuisineTypes\"", StringComparison.Ordinal))
            .index;

        copyIndex.Should().BeLessThan(deleteIndex, "stores must keep their saved category before the global is removed");

        var copySql = UpSqlOperations[copyIndex].Sql;
        copySql.Should().Contain("gen_random_uuid()");
        copySql.Should().Contain("UPDATE \"Stores\"");
        copySql.Should().Contain("SET \"CuisineTypeId\"");
        copySql.Should().Contain("\"IsDefault\", \"StoreId\", \"CreatedAtUtc\"");
        copySql.Should().Contain("false");
    }

    [Fact]
    public void Up_ShouldPreserveLegacyText_WhenPrivateCategoryHasTheSameNormalizedName()
    {
        var copySql = UpSqlOperations
            .Single(x => x.Sql.Contains("_CuisineTypeLegacyCopyMap", StringComparison.Ordinal))
            .Sql;

        // Deterministic target selection: an exact name match wins, then the lowest id.
        copySql.Should().Contain("ORDER BY (pc.\"Name\" = c.\"Name\") DESC, pc.\"Id\" ASC");

        // The conflicting private row keeps its id and store link but receives the exact legacy
        // text, so no unique index is violated and the store value is not lost.
        copySql.Should().Contain("UPDATE \"CuisineTypes\" AS pc");
        copySql.Should().Contain("SET \"Name\" = c.\"Name\"");
        copySql.Should().Contain("pc.\"StoreId\" IS NOT NULL");
        copySql.Should().Contain("pc.\"Name\" <> c.\"Name\"");
        copySql.Should().Contain("WHERE NOT EXISTS");
    }

    [Fact]
    public void Up_ShouldDeleteOnlyGlobalsOutsideTheApprovedList()
    {
        var deleteSql = UpSqlOperations
            .Single(x => x.Sql.Contains("DELETE FROM \"CuisineTypes\"", StringComparison.Ordinal))
            .Sql;

        deleteSql.Should().Contain("\"StoreId\" IS NULL");
        deleteSql.Should().Contain("NOT IN");
        deleteSql.Should().Contain("'acaiteria'");
        deleteSql.Should().Contain("'tapiocaria'");
        deleteSql.Should().NotContain("'lanches'");
        deleteSql.Should().NotContain("\"IsDefault\" = false");
    }

    [Fact]
    public void Up_ShouldMarkOnlyApprovedGlobalsAsDefault()
    {
        var flagSql = UpSqlOperations
            .Single(x => x.Sql.Contains("\"IsDefault\" = (", StringComparison.Ordinal))
            .Sql;

        flagSql.Should().Contain("\"StoreId\" IS NULL");
        flagSql.Should().Contain("'pizzaria'");
        flagSql.Should().Contain("'comida saudavel (fitness)'");
        flagSql.Should().NotContain("'lanches'");
    }

    [Fact]
    public void Up_ShouldPreserveStoresCuisineTypeIdColumn()
    {
        _migration.UpOperations.OfType<DropColumnOperation>()
            .Should().NotContain(x => x.Table == "Stores" && x.Name == "CuisineTypeId");

        UpSql.Should().Contain("UPDATE \"Stores\"");
    }

    [Fact]
    public void Up_ShouldNotTouchCategoriesFromOtherDomains()
    {
        _migration.UpOperations.OfType<SqlOperation>()
            .Should().NotContain(x => x.Sql.Contains("\"ProductCategories\"", StringComparison.Ordinal));
        _migration.UpOperations.OfType<DropTableOperation>()
            .Should().NotContain(x => x.Name == "ProductCategories");
    }

    [Fact]
    public void Down_ShouldNotBlindlyDeleteRows()
    {
        _migration.DownOperations.OfType<DeleteDataOperation>().Should().BeEmpty();
        _migration.DownOperations.OfType<UpdateDataOperation>().Should().BeEmpty();
    }

    [Fact]
    public void Down_ShouldCopyAndRewireReferencedGlobals_BeforeRemovingThem()
    {
        var copyIndex = DownSqlOperations
            .Select((operation, index) => (operation, index))
            .First(x => x.operation.Sql.Contains("_CuisineTypeLegacyCopyMap", StringComparison.Ordinal))
            .index;

        var deleteIndex = DownSqlOperations
            .Select((operation, index) => (operation, index))
            .First(x => x.operation.Sql.Contains("DELETE FROM \"CuisineTypes\"", StringComparison.Ordinal))
            .index;

        copyIndex.Should().BeLessThan(deleteIndex);

        var copySql = DownSqlOperations[copyIndex].Sql;
        copySql.Should().Contain("gen_random_uuid()");
        copySql.Should().Contain("UPDATE \"Stores\"");
        copySql.Should().Contain("SET \"CuisineTypeId\"");
    }

    [Fact]
    public void Down_ShouldPreserveLegacyText_WhenPrivateCategoryHasTheSameNormalizedName()
    {
        var copySql = DownSqlOperations
            .Single(x => x.Sql.Contains("_CuisineTypeLegacyCopyMap", StringComparison.Ordinal))
            .Sql;

        copySql.Should().Contain("ORDER BY (pc.\"Name\" = c.\"Name\") DESC, pc.\"Id\" ASC");
        copySql.Should().Contain("UPDATE \"CuisineTypes\" AS pc");
        copySql.Should().Contain("SET \"Name\" = c.\"Name\"");
        copySql.Should().Contain("pc.\"Name\" <> c.\"Name\"");
    }

    [Fact]
    public void Down_ShouldRestoreRemovedDefaults_GuardedByNotExists()
    {
        var restoreSql = DownRestoreDefaultsOperation.Sql;

        foreach (var name in RemovedDefaultNames)
        {
            restoreSql.Should().Contain($"'{name}'");
        }

        restoreSql.Should().Contain("NOT EXISTS");
        restoreSql.Should().Contain("c.\"StoreId\" IS NULL");
        restoreSql.Should().Contain("'b1000000-0000-0000-0000-000000000003'::uuid");
        restoreSql.Should().Contain("'b1000000-0000-0000-0000-000000000006'::uuid");
        restoreSql.Should().Contain("'b1000000-0000-0000-0000-000000000010'::uuid");
        restoreSql.Should().Contain("'b1000000-0000-0000-0000-000000000011'::uuid");
        restoreSql.Should().Contain("'b1000000-0000-0000-0000-000000000016'::uuid");
    }

    [Fact]
    public void Down_ShouldRestoreLegacyAcaiteriaName()
    {
        var renameSql = DownSqlOperations
            .Single(x => x.Sql.Contains("SET \"Name\" = v.\"Name\"", StringComparison.Ordinal))
            .Sql;

        renameSql.Should().Contain("'Açaiteria'");
        renameSql.Should().Contain("'acaiteria'");
        renameSql.Should().Contain("\"StoreId\" IS NULL");
    }

    [Fact]
    public void Down_ShouldMarkOnlyPreviousApprovedGlobalsAsDefault()
    {
        var flagSql = DownSqlOperations
            .Single(x => x.Sql.Contains("\"IsDefault\" = (", StringComparison.Ordinal))
            .Sql;

        flagSql.Should().Contain("'acaiteria'");
        flagSql.Should().Contain("'sucos e vitaminas'");
        flagSql.Should().NotContain("'acaiiteria'");
    }

    [Fact]
    public void Down_ShouldDeleteOnlyGlobalsOutsideThePreviousApprovedList()
    {
        var deleteSql = DownSqlOperations
            .Single(x => x.Sql.Contains("DELETE FROM \"CuisineTypes\"", StringComparison.Ordinal))
            .Sql;

        deleteSql.Should().Contain("\"StoreId\" IS NULL");
        deleteSql.Should().Contain("'acaiteria'");
        deleteSql.Should().NotContain("'acaiiteria'");
    }
}
