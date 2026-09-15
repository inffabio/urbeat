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

    private string DownSql => string.Join(
        "\n",
        _migration.DownOperations.OfType<SqlOperation>().Select(operation => operation.Sql));

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
        DownSql.Should().Contain("GROUP BY c.\"NormalizedName\"");
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
