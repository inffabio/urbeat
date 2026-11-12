using AutoMapper;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Mappings;
using Urbeat.Domain.Entities;
using Urbeat.Domain.Enums;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Persistence.UnitOfWork;
using Urbeat.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Urbeat.UnitTests.Infrastructure.Services;

public sealed class ProductServiceSpecialClassificationTests : IDisposable
{
    private readonly string _databaseName = $"urbeat-product-special-{Guid.NewGuid()}";
    private readonly IMapper _mapper = new MapperConfiguration(cfg => cfg.AddProfile<EntityToDtoProfile>()).CreateMapper();
    private readonly ApplicationDbContext _db;
    private readonly ProductService _sut;

    public ProductServiceSpecialClassificationTests()
    {
        _db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(_databaseName)
                .Options);
        _sut = new ProductService(_db, _mapper, new EfUnitOfWork(_db), Mock.Of<IImageUploadService>());
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task CreateAsync_ShouldPersistTheSingleSpecialClassification()
    {
        var owner = Guid.NewGuid();
        var store = SeedStore(owner, "loja-a");
        var category = SeedCategory(store.Id);
        await _db.SaveChangesAsync();

        var result = await _sut.CreateAsync(owner, store.Id, new CreateProductRequestDto
        {
            CategoryId = category.Id,
            Name = "X-Bacon",
            Description = "Bacon e cheddar",
            Price = 25.90m,
            ImageUrl = "https://example.com/x-bacon.jpg",
            IsAvailable = true,
            SpecialClassification = ProductSpecialClassification.Offers,
        }, ipAddress: null);

        result.NotFound.Should().BeFalse();
        result.Forbidden.Should().BeFalse();
        result.Product.Should().NotBeNull();
        result.Product!.SpecialClassification.Should().Be(ProductSpecialClassification.Offers);

        var persisted = await _db.Products.SingleAsync();
        persisted.SpecialClassification.Should().Be(ProductSpecialClassification.Offers);
        persisted.IsFeatured.Should().BeFalse();
        persisted.IsBestSeller.Should().BeFalse();
        persisted.IsNew.Should().BeFalse();
    }

    [Fact]
    public async Task CreateAsync_ShouldPersistNull_WhenNoClassificationIsSelected()
    {
        var owner = Guid.NewGuid();
        var store = SeedStore(owner, "loja-a");
        var category = SeedCategory(store.Id);
        await _db.SaveChangesAsync();

        var result = await _sut.CreateAsync(owner, store.Id, new CreateProductRequestDto
        {
            CategoryId = category.Id,
            Name = "X-Salada",
            Price = 21.90m,
            ImageUrl = "https://example.com/x-salada.jpg",
        }, ipAddress: null);

        result.Product!.SpecialClassification.Should().BeNull();
        (await _db.Products.SingleAsync()).SpecialClassification.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_ShouldReplaceAndClearClassification_WithoutRewritingLegacyFlags()
    {
        var owner = Guid.NewGuid();
        var store = SeedStore(owner, "loja-a");
        var category = SeedCategory(store.Id);
        var product = new Product
        {
            StoreId = store.Id,
            CategoryId = category.Id,
            Name = "Brownie",
            Price = 12m,
            ImageUrl = "https://example.com/brownie.jpg",
            IsFeatured = true,
            IsBestSeller = true,
            IsNew = true,
        };
        _db.Products.Add(product);
        await _db.SaveChangesAsync();

        var classify = await _sut.UpdateAsync(owner, product.Id, new UpdateProductRequestDto
        {
            CategoryId = category.Id,
            Name = "Brownie",
            Description = "Chocolate",
            Price = 12m,
            ImageUrl = "https://example.com/brownie.jpg",
            IsAvailable = true,
            SpecialClassification = ProductSpecialClassification.BestSeller,
        }, ipAddress: null);

        classify.NotFound.Should().BeFalse();
        classify.Product!.SpecialClassification.Should().Be(ProductSpecialClassification.BestSeller);

        var afterClassify = await _db.Products.SingleAsync();
        afterClassify.SpecialClassification.Should().Be(ProductSpecialClassification.BestSeller);
        // Legacy flags are kept untouched by the new write path.
        afterClassify.IsFeatured.Should().BeTrue();
        afterClassify.IsBestSeller.Should().BeTrue();
        afterClassify.IsNew.Should().BeTrue();
        // Unrelated product fields survive the classification change.
        afterClassify.CategoryId.Should().Be(category.Id);
        afterClassify.Price.Should().Be(12m);
        afterClassify.ImageUrl.Should().Be("https://example.com/brownie.jpg");

        var clear = await _sut.UpdateAsync(owner, product.Id, new UpdateProductRequestDto
        {
            CategoryId = category.Id,
            Name = "Brownie",
            Description = "Chocolate",
            Price = 12m,
            ImageUrl = "https://example.com/brownie.jpg",
            IsAvailable = true,
            SpecialClassification = null,
        }, ipAddress: null);

        clear.NotFound.Should().BeFalse();
        clear.Product!.SpecialClassification.Should().BeNull();
        (await _db.Products.SingleAsync()).SpecialClassification.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_ShouldRejectClassificationChangesFromAnotherOwner()
    {
        var owner = Guid.NewGuid();
        var store = SeedStore(owner, "loja-a");
        var category = SeedCategory(store.Id);
        var product = new Product
        {
            StoreId = store.Id,
            CategoryId = category.Id,
            Name = "Batata",
            Price = 10m,
            ImageUrl = "https://example.com/batata.jpg",
            SpecialClassification = ProductSpecialClassification.Offers,
        };
        _db.Products.Add(product);
        await _db.SaveChangesAsync();

        var result = await _sut.UpdateAsync(Guid.NewGuid(), product.Id, new UpdateProductRequestDto
        {
            CategoryId = category.Id,
            Name = "Batata",
            Price = 10m,
            ImageUrl = "https://example.com/batata.jpg",
            SpecialClassification = ProductSpecialClassification.New,
        }, ipAddress: null);

        result.Forbidden.Should().BeTrue();
        (await _db.Products.SingleAsync()).SpecialClassification.Should().Be(ProductSpecialClassification.Offers);
    }

    private Store SeedStore(Guid ownerUserId, string slug)
    {
        var store = new Store
        {
            OwnerUserId = ownerUserId,
            Name = "Loja Teste",
            Slug = slug,
            PhoneNumber = "11999999999",
        };
        _db.Stores.Add(store);
        return store;
    }

    private ProductCategory SeedCategory(Guid storeId)
    {
        var category = new ProductCategory
        {
            StoreId = storeId,
            Name = "Lanches",
            DisplayOrder = 1,
            IsActive = true,
        };
        _db.ProductCategories.Add(category);
        return category;
    }
}
