using FluentAssertions;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.UnitTests.Infrastructure.Persistence;

public sealed class CuisineTypeSeederTests : IDisposable
{
    private static readonly string[] ExpectedDefaults =
    [
        "Açaiteria",
        "Cafeteria",
        "Churrascaria",
        "Comida Árabe",
        "Comida Japonesa",
        "Comida Mexicana",
        "Doceria",
        "Hamburgueria",
        "Lanches",
        "Marmitaria",
        "Padaria",
        "Pastelaria",
        "Pizzaria",
        "Sucos e Vitaminas",
        "Tapiocaria"
    ];

    private readonly ApplicationDbContext _db;
    private readonly CuisineTypeSeeder _sut;

    public CuisineTypeSeederTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-cuisine-seeder-{Guid.NewGuid()}")
            .Options;
        _db = new ApplicationDbContext(options);
        _sut = new CuisineTypeSeeder(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task SeedAsync_ShouldCreateExactlyTheFifteenProtectedDefaults()
    {
        await _sut.SeedAsync();

        var defaults = await _db.CuisineTypes.Where(x => x.IsDefault).ToListAsync();

        defaults.Should().HaveCount(15);
        defaults.Select(x => x.Name).Should().BeEquivalentTo(ExpectedDefaults);
        defaults.Should().OnlyContain(x => x.StoreId == null);
        defaults.Should().OnlyContain(x => x.IsActive);
        defaults.Should().OnlyContain(x => x.NormalizedName == CuisineType.NormalizeName(x.Name));
    }

    [Fact]
    public async Task SeedAsync_ShouldBeIdempotent()
    {
        await _sut.SeedAsync();
        await _sut.SeedAsync();

        (await _db.CuisineTypes.CountAsync()).Should().Be(15);
    }

    [Fact]
    public async Task SeedAsync_ShouldNotReturnEarly_WhenOnlyACustomCategoryExists()
    {
        _db.CuisineTypes.Add(new CuisineType { Name = "Comida Baiana", IsActive = true, StoreId = Guid.NewGuid() });
        await _db.SaveChangesAsync();

        await _sut.SeedAsync();

        (await _db.CuisineTypes.CountAsync(x => x.IsDefault)).Should().Be(15);
    }

    [Fact]
    public async Task SeedAsync_ShouldNotReintroduceRemovedLegacyCategories()
    {
        await _sut.SeedAsync();

        var names = await _db.CuisineTypes.Select(x => x.Name).ToListAsync();

        names.Should().NotContain("Cachorro Quente");
        names.Should().NotContain("Tapioca e crepes");
    }

    [Fact]
    public async Task SeedAsync_ShouldNormalizeLegacyDefaultName_WithoutDuplicating()
    {
        _db.CuisineTypes.Add(new CuisineType { Name = "Acaiteria", IsActive = true });
        await _db.SaveChangesAsync();

        await _sut.SeedAsync();

        var defaults = await _db.CuisineTypes.Where(x => x.IsDefault).ToListAsync();

        defaults.Should().HaveCount(15);
        defaults.Select(x => x.Name).Should().Contain("Açaiteria");
        (await _db.CuisineTypes.CountAsync(x => x.NormalizedName == "acaiteria")).Should().Be(1);
    }

    [Fact]
    public async Task SeedAsync_ShouldPreserveStoreOwnedCategories()
    {
        var storeId = Guid.NewGuid();
        _db.CuisineTypes.Add(new CuisineType { Name = "Comida Baiana", IsActive = true, StoreId = storeId });
        await _db.SaveChangesAsync();

        await _sut.SeedAsync();

        var custom = await _db.CuisineTypes.SingleAsync(x => x.Name == "Comida Baiana");
        custom.StoreId.Should().Be(storeId);
        custom.IsDefault.Should().BeFalse();
    }

    [Fact]
    public async Task SeedAsync_ShouldRemoveNonDefaultGlobalCategoryWithoutStoreLink()
    {
        _db.CuisineTypes.Add(new CuisineType { Name = "Comida Baiana", IsActive = true, IsDefault = false, StoreId = null });
        await _db.SaveChangesAsync();

        await _sut.SeedAsync();

        var names = await _db.CuisineTypes.Select(x => x.Name).ToListAsync();
        names.Should().NotContain("Comida Baiana");
    }

    [Fact]
    public async Task SeedAsync_ShouldPreserveProtectedDefaultWithoutStoreLink()
    {
        _db.CuisineTypes.Add(new CuisineType { Name = "Doceria", IsActive = true, IsDefault = true, StoreId = null });
        await _db.SaveChangesAsync();

        await _sut.SeedAsync();

        var names = await _db.CuisineTypes.Select(x => x.Name).ToListAsync();
        names.Should().Contain("Doceria");
    }

    [Fact]
    public async Task SeedAsync_ShouldCopyAndRewireReferencedLegacyGlobalCategory()
    {
        var legacy = new CuisineType { Name = "Comida Baiana", IsActive = true, IsDefault = false, StoreId = null };
        _db.CuisineTypes.Add(legacy);
        var store = new Store
        {
            OwnerUserId = Guid.NewGuid(),
            Name = "Loja Baiana",
            Slug = "loja-baiana",
            CuisineTypeId = legacy.Id
        };
        _db.Stores.Add(store);
        await _db.SaveChangesAsync();

        await _sut.SeedAsync();

        // The global legacy row is removed; each referencing store keeps its original value
        // through a private copy that the store is rewired to.
        (await _db.CuisineTypes.SingleOrDefaultAsync(x => x.Id == legacy.Id)).Should().BeNull();

        var rewiredStore = await _db.Stores.SingleAsync(x => x.Slug == "loja-baiana");
        rewiredStore.CuisineTypeId.Should().NotBeNull();

        var privateCopy = await _db.CuisineTypes.SingleAsync(x => x.Id == rewiredStore.CuisineTypeId);
        privateCopy.Name.Should().Be("Comida Baiana");
        privateCopy.StoreId.Should().Be(store.Id);
        privateCopy.IsDefault.Should().BeFalse();
    }

    [Fact]
    public async Task SeedAsync_ShouldRemoveLegacyDefaultOutsideApprovedList_WhenUnreferenced()
    {
        _db.CuisineTypes.Add(new CuisineType { Name = "Cachorro Quente", IsActive = true, IsDefault = true, StoreId = null });
        await _db.SaveChangesAsync();

        await _sut.SeedAsync();

        var names = await _db.CuisineTypes.Select(x => x.Name).ToListAsync();
        names.Should().NotContain("Cachorro Quente");
        (await _db.CuisineTypes.CountAsync(x => x.IsDefault)).Should().Be(15);
    }

    [Fact]
    public async Task SeedAsync_ShouldClearDefaultFlag_FromLegacyDefaultOutsideApprovedList()
    {
        // A referenced legacy default must not keep IsDefault=true: it is copied per store as a
        // private category so only the 15 approved names remain flagged as defaults.
        var legacy = new CuisineType { Name = "Cachorro Quente", IsActive = true, IsDefault = true, StoreId = null };
        _db.CuisineTypes.Add(legacy);
        var store = new Store
        {
            OwnerUserId = Guid.NewGuid(),
            Name = "Loja Hot Dog",
            Slug = "loja-hot-dog",
            CuisineTypeId = legacy.Id
        };
        _db.Stores.Add(store);
        await _db.SaveChangesAsync();

        await _sut.SeedAsync();

        var defaults = await _db.CuisineTypes.Where(x => x.IsDefault).ToListAsync();
        defaults.Should().HaveCount(15);
        defaults.Should().OnlyContain(x => ExpectedDefaults.Contains(x.Name));

        var privateCopy = await _db.CuisineTypes.SingleAsync(x => x.StoreId == store.Id);
        privateCopy.Name.Should().Be("Cachorro Quente");
        privateCopy.IsDefault.Should().BeFalse();
    }

    [Fact]
    public async Task SeedAsync_ShouldNotRemoveProductCategoriesFromAnotherDomain()
    {
        _db.ProductCategories.Add(new ProductCategory { StoreId = Guid.NewGuid(), Name = "Comida Baiana" });
        await _db.SaveChangesAsync();

        await _sut.SeedAsync();

        var productCategories = await _db.ProductCategories.ToListAsync();
        productCategories.Should().ContainSingle(x => x.Name == "Comida Baiana");
    }
}
