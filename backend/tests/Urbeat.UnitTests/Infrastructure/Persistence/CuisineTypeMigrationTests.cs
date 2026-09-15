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
