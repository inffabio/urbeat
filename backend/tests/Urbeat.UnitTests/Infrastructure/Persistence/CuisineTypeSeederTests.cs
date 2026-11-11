using FluentAssertions;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.UnitTests.Infrastructure.Persistence;

public sealed class CuisineTypeSeederTests : IDisposable
{
    private static readonly string[] ExpectedDefaults =
    [
        "Açaíteria",
        "Batata Recheada",
        "Cachorro-quente (Hot Dog)",
        "Cafeteria",
        "Caldos e Sopas",
        "Comida Japonesa",
        "Comida Saudável (Fitness)",
        "Confeitaria",
        "Creperia",
        "Doceria",
        "Espetinhos e Churrasco",
        "Hamburgueria",
        "Marmitaria",
        "Massas e Lasanhas",
        "Padaria",
        "Pastelaria",
        "Pizzaria",
        "Salgaderia",
        "Sorveteria",
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
    public async Task SeedAsync_ShouldCreateExactlyTheTwentyProtectedDefaults()
    {
        await _sut.SeedAsync();

        var defaults = await _db.CuisineTypes.Where(x => x.IsDefault).ToListAsync();

        defaults.Should().HaveCount(20);
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

        (await _db.CuisineTypes.CountAsync()).Should().Be(20);
        (await _db.CuisineTypes.CountAsync(x => x.IsDefault)).Should().Be(20);
    }

    [Fact]
    public async Task SeedAsync_ShouldNotReturnEarly_WhenOnlyACustomCategoryExists()
    {
        _db.CuisineTypes.Add(new CuisineType { Name = "Comida Baiana", IsActive = true, StoreId = Guid.NewGuid() });
        await _db.SaveChangesAsync();

        await _sut.SeedAsync();

        (await _db.CuisineTypes.CountAsync(x => x.IsDefault)).Should().Be(20);
    }

    [Fact]
    public async Task SeedAsync_ShouldNotReintroduceRemovedLegacyDefaults()
    {
        await _sut.SeedAsync();

        var names = await _db.CuisineTypes.Select(x => x.Name).ToListAsync();

        names.Should().NotContain(["Açaiteria", "Churrascaria", "Comida Árabe", "Comida Mexicana", "Lanches", "Sucos e Vitaminas"]);
    }

    [Fact]
    public async Task SeedAsync_ShouldNormalizeLegacyDefaultName_WithoutDuplicating()
    {
        _db.CuisineTypes.Add(new CuisineType { Name = "PIZZARIA", IsActive = false, IsDefault = false });
        await _db.SaveChangesAsync();

        await _sut.SeedAsync();

        var defaults = await _db.CuisineTypes.Where(x => x.IsDefault).ToListAsync();

        defaults.Should().HaveCount(20);
        defaults.Select(x => x.Name).Should().Contain("Pizzaria");
        (await _db.CuisineTypes.CountAsync(x => x.NormalizedName == "pizzaria")).Should().Be(1);
    }

    [Fact]
    public async Task SeedAsync_ShouldRenameLegacyAcaiteria_WithoutDuplicating()
    {
        // "Açaíteria" keeps the same normalized key as the legacy "Açaiteria", so the existing
        // global row is corrected instead of duplicated.
        _db.CuisineTypes.Add(new CuisineType { Name = "Acaiteria", IsActive = false, IsDefault = true, StoreId = null });
        await _db.SaveChangesAsync();

        await _sut.SeedAsync();

        (await _db.CuisineTypes.CountAsync(x => x.NormalizedName == "acaiteria")).Should().Be(1);
        var renamed = await _db.CuisineTypes.SingleAsync(x => x.NormalizedName == "acaiteria");
        renamed.Name.Should().Be("Açaíteria");
        renamed.IsActive.Should().BeTrue();
        renamed.IsDefault.Should().BeTrue();
        (await _db.CuisineTypes.CountAsync()).Should().Be(20);
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
    public async Task SeedAsync_ShouldCopyAndRewireRemovedLegacyDefaultReferencedByStore()
    {
        // "Lanches" used to be a protected default and is no longer part of the approved list.
        var legacy = new CuisineType { Name = "Lanches", IsActive = true, IsDefault = true, StoreId = null };
        _db.CuisineTypes.Add(legacy);
        var store = new Store
        {
            OwnerUserId = Guid.NewGuid(),
            Name = "Loja Lanches",
            Slug = "loja-lanches",
            CuisineTypeId = legacy.Id
        };
        _db.Stores.Add(store);
        await _db.SaveChangesAsync();

        await _sut.SeedAsync();

        (await _db.CuisineTypes.SingleOrDefaultAsync(x => x.Id == legacy.Id)).Should().BeNull();

        var rewiredStore = await _db.Stores.SingleAsync(x => x.Slug == "loja-lanches");
        var privateCopy = await _db.CuisineTypes.SingleAsync(x => x.Id == rewiredStore.CuisineTypeId);
        privateCopy.Name.Should().Be("Lanches", "the value already saved in the store must not change");
        privateCopy.StoreId.Should().Be(store.Id);
        privateCopy.IsDefault.Should().BeFalse();

        (await _db.CuisineTypes.CountAsync(x => x.IsDefault)).Should().Be(20);
        (await _db.CuisineTypes.CountAsync(x => x.StoreId == null && x.Name == "Lanches")).Should().Be(0);
    }

    [Fact]
    public async Task SeedAsync_ShouldRemoveLegacyDefaultOutsideApprovedList_WhenUnreferenced()
    {
        _db.CuisineTypes.Add(new CuisineType { Name = "Churrascaria", IsActive = true, IsDefault = true, StoreId = null });
        await _db.SaveChangesAsync();

        await _sut.SeedAsync();

        var names = await _db.CuisineTypes.Select(x => x.Name).ToListAsync();
        names.Should().NotContain("Churrascaria");
        (await _db.CuisineTypes.CountAsync(x => x.IsDefault)).Should().Be(20);
    }

    [Fact]
    public async Task SeedAsync_ShouldClearDefaultFlag_FromLegacyDefaultOutsideApprovedList()
    {
        // A referenced legacy default must not keep IsDefault=true: it is copied per store as a
        // private category so only the 20 approved names remain flagged as defaults.
        var legacy = new CuisineType { Name = "Comida Mexicana", IsActive = true, IsDefault = true, StoreId = null };
        _db.CuisineTypes.Add(legacy);
        var store = new Store
        {
            OwnerUserId = Guid.NewGuid(),
            Name = "Loja Mexicana",
            Slug = "loja-mexicana",
            CuisineTypeId = legacy.Id
        };
        _db.Stores.Add(store);
        await _db.SaveChangesAsync();

        await _sut.SeedAsync();

        var defaults = await _db.CuisineTypes.Where(x => x.IsDefault).ToListAsync();
        defaults.Should().HaveCount(20);
        defaults.Should().OnlyContain(x => ExpectedDefaults.Contains(x.Name));

        var privateCopy = await _db.CuisineTypes.SingleAsync(x => x.StoreId == store.Id);
        privateCopy.Name.Should().Be("Comida Mexicana");
        privateCopy.IsDefault.Should().BeFalse();
    }

    [Fact]
    public async Task SeedAsync_ShouldPreserveReferencedLegacyName_WhenStoreAlreadyOwnsConflictingPrivateCategory()
    {
        var legacy = new CuisineType { Name = "Lanches", IsActive = true, IsDefault = true, StoreId = null };
        _db.CuisineTypes.Add(legacy);
        var store = new Store
        {
            OwnerUserId = Guid.NewGuid(),
            Name = "Loja Colisao",
            Slug = "loja-colisao",
            CuisineTypeId = legacy.Id
        };
        _db.Stores.Add(store);
        await _db.SaveChangesAsync();

        // A private category with the same normalized key but different text already belongs to
        // the store. The seeder must not duplicate it nor lose the legacy text saved in the store.
        var conflictingPrivate = new CuisineType { Name = "LANCHES", IsActive = true, StoreId = store.Id };
        _db.CuisineTypes.Add(conflictingPrivate);
        await _db.SaveChangesAsync();

        await _sut.SeedAsync();

        (await _db.CuisineTypes.SingleOrDefaultAsync(x => x.Id == legacy.Id)).Should().BeNull();
        (await _db.CuisineTypes.CountAsync(x => x.StoreId == store.Id && x.NormalizedName == "lanches"))
            .Should().Be(1, "the scoped unique index only allows one private category per normalized key");

        var rewiredStore = await _db.Stores.SingleAsync(x => x.Slug == "loja-colisao");
        var target = await _db.CuisineTypes.SingleAsync(x => x.Id == rewiredStore.CuisineTypeId);

        target.Id.Should().Be(conflictingPrivate.Id, "the existing private row keeps its identity");
        target.Name.Should().Be("Lanches", "the value already saved by the store must be preserved");
        target.StoreId.Should().Be(store.Id);
        target.IsDefault.Should().BeFalse();
    }

    [Fact]
    public async Task SeedAsync_ShouldReusePrivateCategory_WhenItAlreadyHasTheExactLegacyName()
    {
        var legacy = new CuisineType { Name = "Comida Mexicana", IsActive = true, IsDefault = true, StoreId = null };
        _db.CuisineTypes.Add(legacy);
        var store = new Store
        {
            OwnerUserId = Guid.NewGuid(),
            Name = "Loja Mexicana",
            Slug = "loja-mexicana-exata",
            CuisineTypeId = legacy.Id
        };
        _db.Stores.Add(store);
        await _db.SaveChangesAsync();

        var sameNamePrivate = new CuisineType { Name = "Comida Mexicana", IsActive = true, StoreId = store.Id };
        _db.CuisineTypes.Add(sameNamePrivate);
        await _db.SaveChangesAsync();

        await _sut.SeedAsync();

        var rewiredStore = await _db.Stores.SingleAsync(x => x.Slug == "loja-mexicana-exata");
        rewiredStore.CuisineTypeId.Should().Be(sameNamePrivate.Id);
        (await _db.CuisineTypes.CountAsync(x => x.StoreId == store.Id)).Should().Be(1);
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
