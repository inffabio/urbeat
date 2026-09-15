using AutoMapper;
using FluentAssertions;
using Urbeat.Application.Mappings;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.UnitTests.Infrastructure.Services;

public sealed class CuisineTypeServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly CuisineTypeService _sut;

    public CuisineTypeServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-cuisine-service-{Guid.NewGuid()}")
            .Options;
        _db = new ApplicationDbContext(options);

        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<EntityToDtoProfile>()).CreateMapper();
        _sut = new CuisineTypeService(_db, mapper);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task GetActiveAsync_ShouldReturnOnlyGlobalDefaults_NotStorePrivateCategories()
    {
        var storeId = Guid.NewGuid();
        _db.CuisineTypes.AddRange(
            new CuisineType { Name = "Pizzaria", IsDefault = true, IsActive = true, StoreId = null },
            new CuisineType { Name = "Comida Baiana", IsDefault = false, IsActive = true, StoreId = storeId });
        await _db.SaveChangesAsync();

        var result = await _sut.GetActiveAsync();

        result.Select(x => x.Name).Should().Equal("Pizzaria");
    }

    [Fact]
    public async Task GetActiveAsync_ShouldIgnoreInactiveGlobalCategories()
    {
        _db.CuisineTypes.AddRange(
            new CuisineType { Name = "Pizzaria", IsDefault = true, IsActive = true, StoreId = null },
            new CuisineType { Name = "Comida Baiana", IsDefault = false, IsActive = false, StoreId = null });
        await _db.SaveChangesAsync();

        var result = await _sut.GetActiveAsync();

        result.Select(x => x.Name).Should().Equal("Pizzaria");
    }

    [Fact]
    public async Task GetForStoreAsync_ShouldReturnDefaultsPlusOnlyThatStoreCategories()
    {
        var ownerA = Guid.NewGuid();
        var ownerB = Guid.NewGuid();
        var storeA = new Store { OwnerUserId = ownerA, Name = "Loja A", Slug = "loja-a" };
        var storeB = new Store { OwnerUserId = ownerB, Name = "Loja B", Slug = "loja-b" };
        _db.Stores.AddRange(storeA, storeB);
        _db.CuisineTypes.AddRange(
            new CuisineType { Name = "Pizzaria", IsDefault = true, IsActive = true, StoreId = null },
            new CuisineType { Name = "Comida Baiana", IsActive = true, StoreId = storeA.Id },
            new CuisineType { Name = "Comida Mineira", IsActive = true, StoreId = storeB.Id });
        await _db.SaveChangesAsync();

        var result = await _sut.GetForStoreAsync(ownerA, storeA.Id);

        result.Select(x => x.Name).Should().Equal("Comida Baiana", "Pizzaria");
    }

    [Fact]
    public async Task GetForStoreAsync_ShouldReturnEmpty_WhenCallerDoesNotOwnStore()
    {
        var ownerA = Guid.NewGuid();
        var storeA = new Store { OwnerUserId = ownerA, Name = "Loja A", Slug = "loja-a" };
        _db.Stores.Add(storeA);
        _db.CuisineTypes.Add(new CuisineType { Name = "Comida Baiana", IsActive = true, StoreId = storeA.Id });
        await _db.SaveChangesAsync();

        var result = await _sut.GetForStoreAsync(Guid.NewGuid(), storeA.Id);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateForStoreAsync_ShouldCreatePrivateCategory_OwnedByTheStore()
    {
        var owner = Guid.NewGuid();
        var store = new Store { OwnerUserId = owner, Name = "Loja", Slug = "loja" };
        _db.Stores.Add(store);
        await _db.SaveChangesAsync();

        var result = await _sut.CreateForStoreAsync(owner, store.Id, "Comida Baiana");

        result.Should().NotBeNull();
        result!.Name.Should().Be("Comida Baiana");

        var persisted = await _db.CuisineTypes.SingleAsync(x => x.Id == result.Id);
        persisted.StoreId.Should().Be(store.Id);
        persisted.IsDefault.Should().BeFalse();
        persisted.NormalizedName.Should().Be("comida baiana");
    }

    [Fact]
    public async Task CreateForStoreAsync_ShouldRejectDefaultName()
    {
        var owner = Guid.NewGuid();
        var store = new Store { OwnerUserId = owner, Name = "Loja", Slug = "loja" };
        _db.Stores.Add(store);
        _db.CuisineTypes.Add(new CuisineType { Name = "Pizzaria", IsDefault = true, IsActive = true, StoreId = null });
        await _db.SaveChangesAsync();

        var result = await _sut.CreateForStoreAsync(owner, store.Id, "pizzaria");

        result.Should().BeNull();
    }

    [Fact]
    public async Task CreateForStoreAsync_ShouldRejectBlankName()
    {
        var owner = Guid.NewGuid();
        var store = new Store { OwnerUserId = owner, Name = "Loja", Slug = "loja" };
        _db.Stores.Add(store);
        await _db.SaveChangesAsync();

        var result = await _sut.CreateForStoreAsync(owner, store.Id, "   ");

        result.Should().BeNull();
    }

    [Fact]
    public async Task CreateForStoreAsync_ShouldRejectCallerWhoDoesNotOwnStore()
    {
        var owner = Guid.NewGuid();
        var store = new Store { OwnerUserId = owner, Name = "Loja", Slug = "loja" };
        _db.Stores.Add(store);
        await _db.SaveChangesAsync();

        var result = await _sut.CreateForStoreAsync(Guid.NewGuid(), store.Id, "Comida Baiana");

        result.Should().BeNull();
        (await _db.CuisineTypes.CountAsync(x => x.Name == "Comida Baiana")).Should().Be(0);
    }

    [Fact]
    public async Task CreateForStoreAsync_ShouldRejectDuplicateNormalizedNameInSameScope()
    {
        var owner = Guid.NewGuid();
        var store = new Store { OwnerUserId = owner, Name = "Loja", Slug = "loja" };
        _db.Stores.Add(store);
        await _db.SaveChangesAsync();

        var first = await _sut.CreateForStoreAsync(owner, store.Id, "Comida Baiana");
        var second = await _sut.CreateForStoreAsync(owner, store.Id, "COMIDA BAIANA");

        first.Should().NotBeNull();
        second.Should().BeNull();
        (await _db.CuisineTypes.CountAsync(x => x.StoreId == store.Id)).Should().Be(1);
    }

    [Fact]
    public async Task CreateForStoreAsync_ShouldRejectNameCollidingWithExistingDefault()
    {
        var owner = Guid.NewGuid();
        var store = new Store { OwnerUserId = owner, Name = "Loja", Slug = "loja" };
        _db.Stores.Add(store);
        _db.CuisineTypes.Add(new CuisineType { Name = "Comida Árabe", IsDefault = true, IsActive = true, StoreId = null });
        await _db.SaveChangesAsync();

        var result = await _sut.CreateForStoreAsync(owner, store.Id, "comida arabe");

        result.Should().BeNull();
        (await _db.CuisineTypes.CountAsync(x => x.StoreId == store.Id)).Should().Be(0);
    }

    [Fact]
    public async Task DeleteForStoreAsync_ShouldDeleteCustomCategoryNotInUse()
    {
        var owner = Guid.NewGuid();
        var store = new Store { OwnerUserId = owner, Name = "Loja", Slug = "loja" };
        _db.Stores.Add(store);
        var category = new CuisineType { Name = "Comida Baiana", IsActive = true, StoreId = store.Id };
        _db.CuisineTypes.Add(category);
        await _db.SaveChangesAsync();

        var result = await _sut.DeleteForStoreAsync(owner, store.Id, category.Id);

        result.Deleted.Should().BeTrue();
        (await _db.CuisineTypes.CountAsync(x => x.Id == category.Id)).Should().Be(0);
    }

    [Fact]
    public async Task DeleteForStoreAsync_ShouldRejectProtectedDefaultCategory()
    {
        var owner = Guid.NewGuid();
        var store = new Store { OwnerUserId = owner, Name = "Loja", Slug = "loja" };
        _db.Stores.Add(store);
        var global = new CuisineType { Name = "Pizzaria", IsDefault = true, IsActive = true, StoreId = null };
        _db.CuisineTypes.Add(global);
        await _db.SaveChangesAsync();

        var result = await _sut.DeleteForStoreAsync(owner, store.Id, global.Id);

        result.Protected.Should().BeTrue();
        (await _db.CuisineTypes.CountAsync(x => x.Id == global.Id)).Should().Be(1);
    }

    [Fact]
    public async Task DeleteForStoreAsync_ShouldRejectCategoryInUseByTheStore()
    {
        var owner = Guid.NewGuid();
        var store = new Store { OwnerUserId = owner, Name = "Loja", Slug = "loja" };
        _db.Stores.Add(store);
        var category = new CuisineType { Name = "Comida Baiana", IsActive = true, StoreId = store.Id };
        _db.CuisineTypes.Add(category);
        await _db.SaveChangesAsync();
        store.CuisineTypeId = category.Id;
        await _db.SaveChangesAsync();

        var result = await _sut.DeleteForStoreAsync(owner, store.Id, category.Id);

        result.InUse.Should().BeTrue();
        (await _db.CuisineTypes.CountAsync(x => x.Id == category.Id)).Should().Be(1);
    }

    [Fact]
    public async Task DeleteForStoreAsync_ShouldRejectCallerWhoDoesNotOwnStore()
    {
        var owner = Guid.NewGuid();
        var store = new Store { OwnerUserId = owner, Name = "Loja", Slug = "loja" };
        _db.Stores.Add(store);
        var category = new CuisineType { Name = "Comida Baiana", IsActive = true, StoreId = store.Id };
        _db.CuisineTypes.Add(category);
        await _db.SaveChangesAsync();

        var result = await _sut.DeleteForStoreAsync(Guid.NewGuid(), store.Id, category.Id);

        result.Forbidden.Should().BeTrue();
        (await _db.CuisineTypes.CountAsync(x => x.Id == category.Id)).Should().Be(1);
    }

    [Fact]
    public async Task CreateForStoreAsync_ShouldNotCreateCategoryForAnotherStore()
    {
        var ownerA = Guid.NewGuid();
        var ownerB = Guid.NewGuid();
        var storeA = new Store { OwnerUserId = ownerA, Name = "Loja A", Slug = "loja-a" };
        var storeB = new Store { OwnerUserId = ownerB, Name = "Loja B", Slug = "loja-b" };
        _db.Stores.AddRange(storeA, storeB);
        await _db.SaveChangesAsync();

        await _sut.CreateForStoreAsync(ownerA, storeA.Id, "Comida Baiana");
        await _sut.CreateForStoreAsync(ownerB, storeB.Id, "Comida Baiana");

        var categories = await _db.CuisineTypes.Where(x => x.Name == "Comida Baiana").ToListAsync();
        categories.Should().HaveCount(2);
        categories.Select(x => x.StoreId).Should().BeEquivalentTo(new[] { storeA.Id, storeB.Id });
    }
}
