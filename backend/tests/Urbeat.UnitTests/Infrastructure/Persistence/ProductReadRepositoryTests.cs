using AutoMapper;
using FluentAssertions;
using Urbeat.Application.Mappings;
using Urbeat.Domain.Entities;
using Urbeat.Domain.Enums;
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

    [Fact]
    public async Task ListAvailableProductsByStoreAsync_ShouldReturnClassifiedProductsOnceWithTheirClassification()
    {
        var storeId = Guid.NewGuid();
        var category = new ProductCategory { StoreId = storeId, Name = "Lanches", DisplayOrder = 1, IsActive = true };
        _db.ProductCategories.Add(category);
        await _db.SaveChangesAsync();

        _db.Products.AddRange(
            new Product { StoreId = storeId, CategoryId = category.Id, Name = "X-Bacon", Price = 25m, IsAvailable = true, DisplayOrder = 1, SpecialClassification = ProductSpecialClassification.Offers },
            new Product { StoreId = storeId, CategoryId = category.Id, Name = "Batata", Price = 12m, IsAvailable = true, DisplayOrder = 2, SpecialClassification = ProductSpecialClassification.BestSeller },
            new Product { StoreId = storeId, CategoryId = category.Id, Name = "Brownie", Price = 9m, IsAvailable = true, DisplayOrder = 3, SpecialClassification = ProductSpecialClassification.New },
            new Product { StoreId = storeId, CategoryId = category.Id, Name = "X-Salada", Price = 22m, IsAvailable = true, DisplayOrder = 4 });
        await _db.SaveChangesAsync();

        var result = await _sut.ListAvailableProductsByStoreAsync(storeId);

        result.Should().HaveCount(4);
        result.Select(x => x.Id).Should().OnlyHaveUniqueItems();
        result.Single(x => x.Name == "X-Bacon").SpecialClassification.Should().Be(ProductSpecialClassification.Offers);
        result.Single(x => x.Name == "Batata").SpecialClassification.Should().Be(ProductSpecialClassification.BestSeller);
        result.Single(x => x.Name == "Brownie").SpecialClassification.Should().Be(ProductSpecialClassification.New);
        result.Single(x => x.Name == "X-Salada").SpecialClassification.Should().BeNull();
        result.Select(x => x.Name).Should().Equal("X-Bacon", "Batata", "Brownie", "X-Salada");
    }

    [Fact]
    public async Task ListFeaturedProductsByStoreAsync_ShouldReturnOnlyOffers()
    {
        var storeId = Guid.NewGuid();
        var activeCategory = new ProductCategory { StoreId = storeId, Name = "Lanches", DisplayOrder = 1, IsActive = true };
        var inactiveCategory = new ProductCategory { StoreId = storeId, Name = "Inativa", DisplayOrder = 2, IsActive = false };
        _db.ProductCategories.AddRange(activeCategory, inactiveCategory);
        await _db.SaveChangesAsync();

        _db.Products.AddRange(
            new Product { StoreId = storeId, CategoryId = activeCategory.Id, Name = "X-Bacon", Price = 25m, IsAvailable = true, SpecialClassification = ProductSpecialClassification.Offers },
            new Product { StoreId = storeId, CategoryId = activeCategory.Id, Name = "Batata", Price = 12m, IsAvailable = true, SpecialClassification = ProductSpecialClassification.BestSeller },
            new Product { StoreId = storeId, CategoryId = activeCategory.Id, Name = "Brownie", Price = 9m, IsAvailable = true, SpecialClassification = ProductSpecialClassification.New },
            new Product { StoreId = storeId, CategoryId = activeCategory.Id, Name = "Sem classificacao", Price = 7m, IsAvailable = true },
            // Legacy flag alone is not authoritative anymore.
            new Product { StoreId = storeId, CategoryId = activeCategory.Id, Name = "Legado destaque", Price = 8m, IsAvailable = true, IsFeatured = true },
            // Offers in an inactive category must not reach the public catalog.
            new Product { StoreId = storeId, CategoryId = inactiveCategory.Id, Name = "Oferta inativa", Price = 6m, IsAvailable = true, SpecialClassification = ProductSpecialClassification.Offers });
        await _db.SaveChangesAsync();

        var result = await _sut.ListFeaturedProductsByStoreAsync(storeId);

        result.Select(x => x.Name).Should().Equal("X-Bacon");
        result.Single().SpecialClassification.Should().Be(ProductSpecialClassification.Offers);
    }
}
