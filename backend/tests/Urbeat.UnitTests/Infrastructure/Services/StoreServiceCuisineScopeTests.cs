using AutoMapper;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Mappings;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Persistence.UnitOfWork;
using Urbeat.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Urbeat.UnitTests.Infrastructure.Services;

public sealed class StoreServiceCuisineScopeTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly StoreService _sut;

    public StoreServiceCuisineScopeTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-store-cuisine-scope-{Guid.NewGuid()}")
            .Options;
        _db = new ApplicationDbContext(options);

        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<EntityToDtoProfile>()).CreateMapper();
        _sut = new StoreService(
            _db,
            mapper,
            Mock.Of<IStoreReadRepository>(),
            new EfUnitOfWork(_db),
            Mock.Of<IImageUploadService>());
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private async Task<Store> SeedStoreAsync(Guid ownerUserId, string slug, Guid? cuisineTypeId = null)
    {
        var store = new Store
        {
            OwnerUserId = ownerUserId,
            Name = $"Loja {slug}",
            Slug = slug,
            PhoneNumber = "11999999999",
            CuisineTypeId = cuisineTypeId
        };
        _db.Stores.Add(store);
        await _db.SaveChangesAsync();
        return store;
    }

    [Fact]
    public async Task CreateForOwnerAsync_ShouldResolveGlobalCategory_IgnoringAccentsAndCase()
    {
        var global = new CuisineType { Name = "Comida Árabe", IsDefault = true, IsActive = true, StoreId = null };
        _db.CuisineTypes.Add(global);
        await _db.SaveChangesAsync();

        var ownerUserId = Guid.NewGuid();
        var result = await _sut.CreateForOwnerAsync(ownerUserId, new CreateStoreRequestDto
        {
            Name = "Loja Teste",
            Slug = "loja-teste",
            PhoneNumber = "11999999999",
            CuisineType = "comida arabe"
        }, ipAddress: null);

        result.Created.Should().BeTrue();
        var store = await _db.Stores.SingleAsync(x => x.OwnerUserId == ownerUserId);
        store.CuisineTypeId.Should().Be(global.Id);
    }

    [Fact]
    public async Task CreateForOwnerAsync_ShouldCreatePrivateCopy_WhenNameIsCustomAndOwnedByAnotherStore()
    {
        var otherOwner = Guid.NewGuid();
        var otherStore = await SeedStoreAsync(otherOwner, "outra-loja");
        var foreignCategory = new CuisineType
        {
            Name = "Comida Baiana",
            IsActive = true,
            StoreId = otherStore.Id
        };
        _db.CuisineTypes.Add(foreignCategory);
        await _db.SaveChangesAsync();

        var ownerUserId = Guid.NewGuid();
        var result = await _sut.CreateForOwnerAsync(ownerUserId, new CreateStoreRequestDto
        {
            Name = "Loja Teste",
            Slug = "loja-teste",
            PhoneNumber = "11999999999",
            CuisineType = "Comida Baiana"
        }, ipAddress: null);

        result.Created.Should().BeTrue();
        var store = await _db.Stores.SingleAsync(x => x.OwnerUserId == ownerUserId);
        store.CuisineTypeId.Should().NotBe(foreignCategory.Id);

        var privateCopy = await _db.CuisineTypes.SingleAsync(x => x.StoreId == store.Id);
        privateCopy.Name.Should().Be("Comida Baiana");
        privateCopy.IsDefault.Should().BeFalse();
        store.CuisineTypeId.Should().Be(privateCopy.Id);
        foreignCategory.StoreId.Should().Be(otherStore.Id);
    }

    [Fact]
    public async Task CreateForOwnerAsync_ShouldCreatePrivateCategory_ForCustomName()
    {
        var ownerUserId = Guid.NewGuid();
        var result = await _sut.CreateForOwnerAsync(ownerUserId, new CreateStoreRequestDto
        {
            Name = "Loja Teste",
            Slug = "loja-teste",
            PhoneNumber = "11999999999",
            CuisineType = "Comida Baiana"
        }, ipAddress: null);

        result.Created.Should().BeTrue();
        var store = await _db.Stores.SingleAsync(x => x.OwnerUserId == ownerUserId);
        var privateCategory = await _db.CuisineTypes.SingleAsync(x => x.StoreId == store.Id);
        privateCategory.Name.Should().Be("Comida Baiana");
        privateCategory.IsDefault.Should().BeFalse();
        store.CuisineTypeId.Should().Be(privateCategory.Id);
    }

    [Fact]
    public async Task UpdateAsync_ShouldResolveGlobalDefault_IgnoringAccents()
    {
        var global = new CuisineType { Name = "Comida Árabe", IsDefault = true, IsActive = true, StoreId = null };
        _db.CuisineTypes.Add(global);
        var ownerUserId = Guid.NewGuid();
        var store = await SeedStoreAsync(ownerUserId, "loja-teste");
        await _db.SaveChangesAsync();

        var result = await _sut.UpdateAsync(ownerUserId, store.Id, new UpdateStoreRequestDto
        {
            Name = "Loja Teste",
            PhoneNumber = "11999999999",
            CuisineType = "comida arabe"
        }, ipAddress: null);

        result.InvalidCuisineType.Should().BeFalse();
        result.Store.Should().NotBeNull();
        (await _db.Stores.SingleAsync(x => x.Id == store.Id)).CuisineTypeId.Should().Be(global.Id);
    }

    [Fact]
    public async Task UpdateAsync_ShouldAllowCategoryOwnedByTheSameStore()
    {
        var ownerUserId = Guid.NewGuid();
        var store = await SeedStoreAsync(ownerUserId, "loja-teste");
        var ownCategory = new CuisineType { Name = "Comida Baiana", IsActive = true, StoreId = store.Id };
        _db.CuisineTypes.Add(ownCategory);
        await _db.SaveChangesAsync();

        var result = await _sut.UpdateAsync(ownerUserId, store.Id, new UpdateStoreRequestDto
        {
            Name = "Loja Teste",
            PhoneNumber = "11999999999",
            CuisineType = "comida baiana"
        }, ipAddress: null);

        result.InvalidCuisineType.Should().BeFalse();
        (await _db.Stores.SingleAsync(x => x.Id == store.Id)).CuisineTypeId.Should().Be(ownCategory.Id);
    }

    [Fact]
    public async Task UpdateAsync_ShouldRejectCategoryOwnedByAnotherStore()
    {
        var ownerUserId = Guid.NewGuid();
        var store = await SeedStoreAsync(ownerUserId, "loja-teste");

        var otherOwner = Guid.NewGuid();
        var otherStore = await SeedStoreAsync(otherOwner, "outra-loja");
        var foreignCategory = new CuisineType { Name = "Comida Baiana", IsActive = true, StoreId = otherStore.Id };
        _db.CuisineTypes.Add(foreignCategory);
        await _db.SaveChangesAsync();

        var result = await _sut.UpdateAsync(ownerUserId, store.Id, new UpdateStoreRequestDto
        {
            Name = "Loja Teste",
            PhoneNumber = "11999999999",
            CuisineType = "Comida Baiana"
        }, ipAddress: null);

        result.InvalidCuisineType.Should().BeTrue();
        (await _db.Stores.SingleAsync(x => x.Id == store.Id)).CuisineTypeId.Should().NotBe(foreignCategory.Id);
    }

    [Fact]
    public async Task UpdateAsync_ShouldPreferOwnCategory_WhenAnotherStoreHasTheSameName()
    {
        var ownerUserId = Guid.NewGuid();
        var store = await SeedStoreAsync(ownerUserId, "loja-teste");
        var ownCategory = new CuisineType { Name = "Comida Baiana", IsActive = true, StoreId = store.Id };

        var otherOwner = Guid.NewGuid();
        var otherStore = await SeedStoreAsync(otherOwner, "outra-loja");
        var foreignCategory = new CuisineType { Name = "Comida Baiana", IsActive = true, StoreId = otherStore.Id };

        _db.CuisineTypes.AddRange(ownCategory, foreignCategory);
        await _db.SaveChangesAsync();

        var result = await _sut.UpdateAsync(ownerUserId, store.Id, new UpdateStoreRequestDto
        {
            Name = "Loja Teste",
            PhoneNumber = "11999999999",
            CuisineType = "COMIDA BAIANA"
        }, ipAddress: null);

        result.InvalidCuisineType.Should().BeFalse();
        (await _db.Stores.SingleAsync(x => x.Id == store.Id)).CuisineTypeId.Should().Be(ownCategory.Id);
    }

    [Fact]
    public async Task UpdateAsync_ShouldRejectInactiveCategory()
    {
        var ownerUserId = Guid.NewGuid();
        var store = await SeedStoreAsync(ownerUserId, "loja-teste");
        var inactive = new CuisineType { Name = "Comida Baiana", IsActive = false, StoreId = store.Id };
        _db.CuisineTypes.Add(inactive);
        await _db.SaveChangesAsync();

        var result = await _sut.UpdateAsync(ownerUserId, store.Id, new UpdateStoreRequestDto
        {
            Name = "Loja Teste",
            PhoneNumber = "11999999999",
            CuisineType = "Comida Baiana"
        }, ipAddress: null);

        result.InvalidCuisineType.Should().BeTrue();
    }
}
