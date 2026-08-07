using AutoMapper;
using FluentAssertions;
using Urbeat.Application.Mappings;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Persistence.ReadRepositories;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.UnitTests.Infrastructure.Persistence;

public sealed class ProductReadRepositoryTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly ProductReadRepository _sut;

    public ProductReadRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-prr-{Guid.NewGuid()}")
            .Options;
        _db = new ApplicationDbContext(options);

        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<EntityToDtoProfile>()).CreateMapper();
        _sut = new ProductReadRepository(_db, mapper);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task ListCategoriesByStoreAsync_ShouldReturnOnlyActiveCategoriesWithAvailableProductsFromSameStore()
    {
        var storeId = Guid.NewGuid();
        var otherStoreId = Guid.NewGuid();
        var categoryWithProduct = new ProductCategory { StoreId = storeId, Name = "Lanches", DisplayOrder = 1 };
        var emptyCategory = new ProductCategory { StoreId = storeId, Name = "Bebidas", DisplayOrder = 2 };
        var inactiveCategory = new ProductCategory { StoreId = storeId, Name = "Inativa", DisplayOrder = 3, IsActive = false };
        var otherStoreCategory = new ProductCategory { StoreId = otherStoreId, Name = "Outra loja", DisplayOrder = 4 };
        _db.ProductCategories.AddRange(categoryWithProduct, emptyCategory, inactiveCategory, otherStoreCategory);
        await _db.SaveChangesAsync();

        _db.Products.AddRange(
            new Product { StoreId = storeId, CategoryId = categoryWithProduct.Id, Name = "X-burguer", Price = 20m, IsAvailable = true },
            new Product { StoreId = storeId, CategoryId = emptyCategory.Id, Name = "Indisponivel", Price = 8m, IsAvailable = false },
            new Product { StoreId = storeId, CategoryId = otherStoreCategory.Id, Name = "Categoria errada", Price = 10m, IsAvailable = true });
        await _db.SaveChangesAsync();

        var result = await _sut.ListCategoriesByStoreAsync(storeId);

        result.Select(x => x.Name).Should().Equal("Lanches");
    }

    [Fact]
    public async Task ListAvailableProductsByStoreAsync_ShouldIgnoreProductsWhoseCategoryBelongsToAnotherStore()
    {
        var storeId = Guid.NewGuid();
        var otherStoreId = Guid.NewGuid();
        var ownCategory = new ProductCategory { StoreId = storeId, Name = "Lanches" };
        var otherStoreCategory = new ProductCategory { StoreId = otherStoreId, Name = "Outra loja" };
        _db.ProductCategories.AddRange(ownCategory, otherStoreCategory);
        await _db.SaveChangesAsync();

        _db.Products.AddRange(
            new Product { StoreId = storeId, CategoryId = ownCategory.Id, Name = "X-burguer", Price = 20m, IsAvailable = true },
            new Product { StoreId = storeId, CategoryId = otherStoreCategory.Id, Name = "Produto contaminado", Price = 10m, IsAvailable = true });
        await _db.SaveChangesAsync();

        var result = await _sut.ListAvailableProductsByStoreAsync(storeId);

        result.Select(x => x.Name).Should().Equal("X-burguer");
    }
}
