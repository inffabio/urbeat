using AutoMapper;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Mappings;
using Urbeat.Domain.Entities;
using Urbeat.Domain.Enums;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Persistence.UnitOfWork;
using Urbeat.Infrastructure.Services;

namespace Urbeat.UnitTests.Infrastructure.Services;

/// <summary>
/// The legacy batch endpoint replaces the whole store catalog, so a seller sync must be able to
/// send the single special classification and keep it across a resync instead of wiping it.
/// </summary>
public sealed class ProductServiceBatchSpecialClassificationTests : IDisposable
{
    private readonly string _databaseName = $"urbeat-product-batch-special-{Guid.NewGuid()}";
    private readonly IMapper _mapper = new MapperConfiguration(cfg => cfg.AddProfile<EntityToDtoProfile>()).CreateMapper();
    private readonly Mock<IImageUploadService> _imageUpload = new();
    private readonly ApplicationDbContext _db;
    private readonly ProductService _sut;

    public ProductServiceBatchSpecialClassificationTests()
    {
        _imageUpload
            .Setup(service => service.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(_databaseName)
                .Options);
        _sut = new ProductService(_db, _mapper, new EfUnitOfWork(_db), _imageUpload.Object);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task BatchUpsertAsync_ShouldCreateTheProductWithOnlyTheSingleClassification()
    {
        var owner = Guid.NewGuid();
        var store = SeedStore(owner, "loja-a");
        var category = SeedCategory(store.Id);
        await _db.SaveChangesAsync();

        var created = await _sut.BatchUpsertAsync(owner, store.Id, Batch(
            Item(category.Id, "X-Bacon", ProductSpecialClassification.Offers)), ipAddress: null);

        created.Should().ContainSingle();
        created.Single().SpecialClassification.Should().Be(ProductSpecialClassification.Offers);

        var persisted = await _db.Products.SingleAsync();
        persisted.SpecialClassification.Should().Be(ProductSpecialClassification.Offers);
        // The batch never writes the legacy booleans, which remain compatibility-only.
        persisted.IsFeatured.Should().BeFalse();
        persisted.IsBestSeller.Should().BeFalse();
        persisted.IsNew.Should().BeFalse();
    }

    [Fact]
    public async Task BatchUpsertAsync_ShouldReplacePreserveAndClearTheClassificationAcrossResyncs()
    {
        var owner = Guid.NewGuid();
        var store = SeedStore(owner, "loja-a");
        var category = SeedCategory(store.Id);
        await _db.SaveChangesAsync();

        await _sut.BatchUpsertAsync(owner, store.Id, Batch(
            Item(category.Id, "X-Bacon", ProductSpecialClassification.Offers)), ipAddress: null);

        // A resync that sends the new classification replaces the previous one.
        var replaced = await _sut.BatchUpsertAsync(owner, store.Id, Batch(
            Item(category.Id, "X-Bacon", ProductSpecialClassification.BestSeller)), ipAddress: null);
        replaced.Single().SpecialClassification.Should().Be(ProductSpecialClassification.BestSeller);
        (await _db.Products.SingleAsync()).SpecialClassification.Should().Be(ProductSpecialClassification.BestSeller);

        // A resync that keeps sending the same classification preserves it.
        var preserved = await _sut.BatchUpsertAsync(owner, store.Id, Batch(
            Item(category.Id, "X-Bacon", ProductSpecialClassification.BestSeller)), ipAddress: null);
        preserved.Single().SpecialClassification.Should().Be(ProductSpecialClassification.BestSeller);
        (await _db.Products.SingleAsync()).SpecialClassification.Should().Be(ProductSpecialClassification.BestSeller);

        // An explicit null clears the classification without touching the legacy flags.
        var cleared = await _sut.BatchUpsertAsync(owner, store.Id, Batch(
            Item(category.Id, "X-Bacon", null)), ipAddress: null);
        cleared.Single().SpecialClassification.Should().BeNull();

        var persisted = await _db.Products.SingleAsync();
        persisted.SpecialClassification.Should().BeNull();
        persisted.IsFeatured.Should().BeFalse();
        persisted.IsBestSeller.Should().BeFalse();
        persisted.IsNew.Should().BeFalse();
    }

    [Fact]
    public async Task BatchUpsertAsync_ShouldOnlyWriteTheFieldsPresentedByThePayload()
    {
        var owner = Guid.NewGuid();
        var store = SeedStore(owner, "loja-a");
        var category = SeedCategory(store.Id);
        var otherCategory = SeedCategory(store.Id, "Bebidas");
        await _db.SaveChangesAsync();

        var created = await _sut.BatchUpsertAsync(owner, store.Id, Batch(
            Item(otherCategory.Id, "Coca-Cola", ProductSpecialClassification.New)), ipAddress: null);

        created.Single().CategoryId.Should().Be(otherCategory.Id);
        created.Single().Price.Should().Be(9.90m);
        created.Single().ImageUrl.Should().Be("https://example.com/coca-cola.jpg");
        created.Single().Additionals.Should().BeEmpty();
        created.Single().ChoiceOptions.Should().BeEmpty();
        created.Single().Variations.Should().BeEmpty();

        var persisted = await _db.Products.SingleAsync();
        persisted.CategoryId.Should().Be(otherCategory.Id);
        persisted.Price.Should().Be(9.90m);
        persisted.ImageUrl.Should().Be("https://example.com/coca-cola.jpg");
        persisted.SpecialClassification.Should().Be(ProductSpecialClassification.New);
    }

    [Fact]
    public async Task BatchUpsertAsync_ShouldNotChangeAnotherStoresProducts()
    {
        var owner = Guid.NewGuid();
        var store = SeedStore(owner, "loja-a");
        var category = SeedCategory(store.Id);
        var otherOwner = Guid.NewGuid();
        var otherStore = SeedStore(otherOwner, "loja-b");
        var otherCategory = SeedCategory(otherStore.Id);
        _db.Products.Add(new Product
        {
            StoreId = otherStore.Id,
            CategoryId = otherCategory.Id,
            Name = "X-Salada",
            Price = 15m,
            ImageUrl = "https://example.com/x-salada.jpg",
            SpecialClassification = ProductSpecialClassification.Offers,
        });
        await _db.SaveChangesAsync();

        var result = await _sut.BatchUpsertAsync(owner, otherStore.Id, Batch(
            Item(category.Id, "X-Bacon", ProductSpecialClassification.Offers)), ipAddress: null);

        result.Should().BeEmpty();
        var untouched = await _db.Products.SingleAsync();
        untouched.Name.Should().Be("X-Salada");
        untouched.SpecialClassification.Should().Be(ProductSpecialClassification.Offers);
    }

    [Fact]
    public async Task BatchUpsertAsync_ShouldRejectANullItemsCollectionBeforeTouchingTheCatalog()
    {
        var owner = Guid.NewGuid();
        var store = SeedStore(owner, "loja-a");
        var category = SeedCategory(store.Id);
        _db.Products.Add(new Product
        {
            StoreId = store.Id,
            CategoryId = category.Id,
            Name = "X-Salada",
            Price = 15m,
            ImageUrl = "https://example.com/x-salada.jpg",
        });
        await _db.SaveChangesAsync();

        var act = () => _sut.BatchUpsertAsync(
            owner, store.Id, new BatchUpsertProductsRequestDto { Items = null }, ipAddress: null);

        await act.Should().ThrowAsync<ArgumentException>();

        var persisted = await _db.Products.ToListAsync();
        persisted.Should().ContainSingle(product => product.Name == "X-Salada");
        _imageUpload.Verify(
            service => service.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static BatchUpsertProductsRequestDto Batch(params BatchProductItemDto[] items)
        => new() { Items = items };

    private static BatchProductItemDto Item(
        Guid categoryId, string name, ProductSpecialClassification? classification)
        => new()
        {
            CategoryId = categoryId,
            Name = name,
            Description = "Produto do batch",
            Price = 9.90m,
            ImageUrl = $"https://example.com/{name.ToLowerInvariant()}.jpg",
            IsAvailable = true,
            DisplayOrder = 1,
            SpecialClassification = classification,
        };

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

    private ProductCategory SeedCategory(Guid storeId, string name = "Lanches")
    {
        var category = new ProductCategory
        {
            StoreId = storeId,
            Name = name,
            DisplayOrder = 1,
            IsActive = true,
        };
        _db.ProductCategories.Add(category);
        return category;
    }
}
