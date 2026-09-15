using FluentAssertions;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.UnitTests.Infrastructure.Persistence;

public sealed class CuisineTypeConfigurationTests : IDisposable
{
    private readonly ApplicationDbContext _db;

    public CuisineTypeConfigurationTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-cuisine-config-{Guid.NewGuid()}")
            .Options;
        _db = new ApplicationDbContext(options);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public void DefaultCuisineType_ShouldBeFlaggedAsDefault_AndOwnedByNoStore()
    {
        var defaultCuisine = new CuisineType { Name = "Pizzaria", IsDefault = true, StoreId = null };

        defaultCuisine.IsDefault.Should().BeTrue();
        defaultCuisine.StoreId.Should().BeNull();
        defaultCuisine.Store.Should().BeNull();
    }

    [Fact]
    public void CustomCuisineType_ShouldNotBeDefault_AndBelongToOneStore()
    {
        var storeId = Guid.NewGuid();
        var customCuisine = new CuisineType { Name = "Comida Baiana", IsDefault = false, StoreId = storeId };

        customCuisine.IsDefault.Should().BeFalse();
        customCuisine.StoreId.Should().Be(storeId);
    }

    [Fact]
    public void CuisineType_ShouldDeriveNormalizedName_IgnoringCaseAccentsAndSurroundingSpaces()
    {
        var cuisine = new CuisineType { Name = "  Comida Árabe " };

        cuisine.NormalizedName.Should().Be("comida arabe");
    }

    [Fact]
    public void CuisineType_ShouldHaveNullableStoreForeignKey_WithRestrictDeleteBehavior()
    {
        var entity = _db.Model.FindEntityType(typeof(CuisineType));

        entity.Should().NotBeNull();
        var storeFk = entity!.GetForeignKeys().Single(fk => fk.PrincipalEntityType.ClrType == typeof(Store));

        storeFk.IsRequired.Should().BeFalse();
        storeFk.DeleteBehavior.Should().Be(DeleteBehavior.Restrict);
        storeFk.Properties.Select(property => property.Name).Should().Equal(nameof(CuisineType.StoreId));
    }

    [Fact]
    public void CuisineType_ShouldHaveUniqueIndex_ScopedByStore_OnNormalizedName()
    {
        var entity = _db.Model.FindEntityType(typeof(CuisineType));

        entity.Should().NotBeNull();
        var index = entity!.GetIndexes().Single(i =>
            i.Properties.Select(property => property.Name).SequenceEqual(new[]
            {
                nameof(CuisineType.StoreId),
                nameof(CuisineType.NormalizedName)
            }));

        index.IsUnique.Should().BeTrue();
        index.GetFilter().Should().NotBeNull();
        index.GetFilter().Should().Contain(nameof(CuisineType.StoreId));
    }

    [Fact]
    public void CuisineType_ShouldProtectGlobalDefaultNames_WithFilteredUniqueIndex()
    {
        var entity = _db.Model.FindEntityType(typeof(CuisineType));

        entity.Should().NotBeNull();
        var index = entity!.GetIndexes().Single(i =>
            i.Properties.Select(property => property.Name).SequenceEqual(new[] { nameof(CuisineType.NormalizedName) }));

        index.IsUnique.Should().BeTrue();
        index.GetFilter().Should().NotBeNull();
        index.GetFilter().Should().Contain(nameof(CuisineType.StoreId));
    }

    [Fact]
    public void CuisineType_ShouldRequireNormalizedName_WithMaxLength()
    {
        var entity = _db.Model.FindEntityType(typeof(CuisineType));

        var property = entity!.FindProperty(nameof(CuisineType.NormalizedName));

        property.Should().NotBeNull();
        property!.IsNullable.Should().BeFalse();
        property.GetMaxLength().Should().Be(80);
    }
}
