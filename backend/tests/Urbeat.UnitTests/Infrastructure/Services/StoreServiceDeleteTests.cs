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
using Xunit;

namespace Urbeat.UnitTests.Infrastructure.Services;

public sealed class StoreServiceDeleteTests : IDisposable
{
    private const string CloudinaryBaseUrl = "https://res.cloudinary.com/urbeat/image/upload/v1712345678/";

    private readonly ApplicationDbContext _db;
    private readonly RecordingImageUploadService _imageUploadService = new();
    private readonly StoreService _sut;

    public StoreServiceDeleteTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-store-delete-{Guid.NewGuid()}")
            .Options;
        _db = new ApplicationDbContext(options);

        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<EntityToDtoProfile>()).CreateMapper();

        _sut = new StoreService(
            _db,
            mapper,
            Mock.Of<IStoreReadRepository>(),
            new EfUnitOfWork(_db),
            _imageUploadService);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task DeleteForOwnerAsync_ShouldRemoveStoreScopedRows_AndKeepGlobalAndOtherStoreData()
    {
        var ownerUserId = Guid.NewGuid();
        var defaultCuisine = new CuisineType { Name = "Pizza", IsActive = true, IsDefault = true };
        _db.CuisineTypes.Add(defaultCuisine);

        var store = CreateStore(ownerUserId, defaultCuisine.Id, "loja-a");
        var privateCuisine = new CuisineType
        {
            Name = "Culinária da Loja",
            NormalizedName = "culinaria da loja",
            IsActive = true,
            IsDefault = false,
            StoreId = store.Id
        };
        _db.CuisineTypes.Add(privateCuisine);
        store.CuisineTypeId = privateCuisine.Id;

        var category = new ProductCategory { StoreId = store.Id, Name = "Pratos" };
        _db.ProductCategories.Add(category);

        _db.Products.AddRange(
            new Product { StoreId = store.Id, CategoryId = category.Id, Name = "Pizza", ImageUrl = CloudinaryBaseUrl + "stores/loja-a/products/pizza.jpg" },
            new Product { StoreId = store.Id, CategoryId = category.Id, Name = "Sem foto" });
        _db.SellerSubscriptions.Add(new SellerSubscription
        {
            StoreId = store.Id,
            SellerUserId = ownerUserId,
            PlanName = "Starter",
            StartDateUtc = DateTime.UtcNow,
            NextBillingDateUtc = DateTime.UtcNow.AddDays(15)
        });

        var otherStore = CreateStore(Guid.NewGuid(), defaultCuisine.Id, "loja-b");
        _db.Products.Add(new Product
        {
            StoreId = otherStore.Id,
            CategoryId = Guid.NewGuid(),
            Name = "Produto de outra loja",
            ImageUrl = CloudinaryBaseUrl + "stores/loja-b/products/outro.jpg"
        });

        var globalNeighborhood = new DeliveryNeighborhood
        {
            City = "Sao Paulo",
            Neighborhood = "Centro",
            NormalizedName = "centro",
            IsActive = true
        };
        _db.DeliveryNeighborhoods.Add(globalNeighborhood);

        await _db.SaveChangesAsync();

        var result = await _sut.DeleteForOwnerAsync(ownerUserId, store.Id, "127.0.0.1");

        result.Deleted.Should().BeTrue();
        result.NotFound.Should().BeFalse();
        result.Forbidden.Should().BeFalse();

        // Store, its private category, products and subscription are gone.
        (await _db.Stores.AnyAsync(x => x.Id == store.Id)).Should().BeFalse();
        (await _db.CuisineTypes.AnyAsync(x => x.Id == privateCuisine.Id)).Should().BeFalse();
        (await _db.Products.AnyAsync(x => x.StoreId == store.Id)).Should().BeFalse();
        (await _db.ProductCategories.AnyAsync(x => x.StoreId == store.Id)).Should().BeFalse();
        (await _db.SellerSubscriptions.AnyAsync(x => x.StoreId == store.Id)).Should().BeFalse();

        // Global reference data and other stores survive.
        (await _db.CuisineTypes.AnyAsync(x => x.Id == defaultCuisine.Id)).Should().BeTrue();
        (await _db.DeliveryNeighborhoods.AnyAsync(x => x.Id == globalNeighborhood.Id)).Should().BeTrue();
        (await _db.Stores.AnyAsync(x => x.Id == otherStore.Id)).Should().BeTrue();
        (await _db.Products.AnyAsync(x => x.StoreId == otherStore.Id)).Should().BeTrue();

        // Best-effort Cloudinary cleanup is attempted for store and product images.
        _imageUploadService.DeletedUrls.Should().BeEquivalentTo(new[]
        {
            store.LogoUrl!,
            store.BannerUrl!,
            CloudinaryBaseUrl + "stores/loja-a/products/pizza.jpg"
        });
        result.AssetsAttempted.Should().Be(3);
        result.AssetsDeleted.Should().Be(3);
        result.AssetsFailed.Should().Be(0);

        var audit = await _db.AuditLogs.SingleAsync(x => x.Event == "StoreDeleted");
        audit.EntityId.Should().Be(store.Id);
    }

    [Fact]
    public async Task DeleteForOwnerAsync_ShouldKeepDatabaseMutation_WhenCloudinaryDeletionFails()
    {
        var ownerUserId = Guid.NewGuid();
        var store = CreateStore(ownerUserId, null, "loja-cloudinary-falha");
        await _db.SaveChangesAsync();

        _imageUploadService.FailForUrl = store.LogoUrl;

        var result = await _sut.DeleteForOwnerAsync(ownerUserId, store.Id, null);

        result.Deleted.Should().BeTrue();
        result.AssetsFailed.Should().Be(1);
        result.AssetsDeleted.Should().Be(1);
        (await _db.Stores.AnyAsync(x => x.Id == store.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteForOwnerAsync_ShouldCallCloudinaryOnlyAfterTheDatabaseCommit()
    {
        var ownerUserId = Guid.NewGuid();
        var store = CreateStore(ownerUserId, null, "loja-pos-commit");
        await _db.SaveChangesAsync();

        var storeId = store.Id;
        var storeStillExistedDuringCleanup = true;
        _imageUploadService.OnDelete = _ =>
        {
            storeStillExistedDuringCleanup = _db.Stores.Any(x => x.Id == storeId);
        };

        var result = await _sut.DeleteForOwnerAsync(ownerUserId, storeId, null);

        result.Deleted.Should().BeTrue();
        storeStillExistedDuringCleanup.Should().BeFalse("Cloudinary cleanup must happen only after the EF transaction commits");
    }

    [Fact]
    public async Task DeleteForOwnerAsync_ShouldReturnForbidden_AndKeepStore_WhenCallerIsNotOwner()
    {
        var ownerUserId = Guid.NewGuid();
        var store = CreateStore(ownerUserId, null, "loja-forbidden");
        await _db.SaveChangesAsync();

        (await _db.Stores.CountAsync()).Should().Be(1);
        (await _db.Stores.SingleAsync()).Id.Should().Be(store.Id);
        (await _db.Stores.SingleAsync()).OwnerUserId.Should().Be(ownerUserId);

        var result = await _sut.DeleteForOwnerAsync(Guid.NewGuid(), store.Id, null);

        result.NotFound.Should().BeFalse();
        result.Forbidden.Should().BeTrue();
        result.Deleted.Should().BeFalse();
        (await _db.Stores.AnyAsync(x => x.Id == store.Id)).Should().BeTrue();
        _imageUploadService.DeletedUrls.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteForOwnerAsync_ShouldReturnNotFound_WhenStoreDoesNotExist()
    {
        var result = await _sut.DeleteForOwnerAsync(Guid.NewGuid(), Guid.NewGuid(), null);

        result.NotFound.Should().BeTrue();
        result.Deleted.Should().BeFalse();
        _imageUploadService.DeletedUrls.Should().BeEmpty();
    }

    private Store CreateStore(Guid ownerUserId, Guid? cuisineTypeId, string slug)
    {
        var store = new Store
        {
            OwnerUserId = ownerUserId,
            Name = $"Loja {slug}",
            Slug = slug,
            PhoneNumber = "11999999999",
            CuisineTypeId = cuisineTypeId,
            LogoUrl = CloudinaryBaseUrl + $"stores/{slug}/logo/logo.png",
            BannerUrl = CloudinaryBaseUrl + $"stores/{slug}/banner/banner.png"
        };
        _db.Stores.Add(store);
        return store;
    }

    private sealed class RecordingImageUploadService : IImageUploadService
    {
        public List<string> DeletedUrls { get; } = new();

        public string? FailForUrl { get; set; }

        public Action<string>? OnDelete { get; set; }

        public Task<string> UploadAsync(Stream fileStream, string fileName, string folder, CancellationToken cancellationToken = default)
            => Task.FromResult($"https://res.cloudinary.com/urbeat/image/upload/{folder}/{fileName}");

        public Task DeleteAsync(string imageUrl, CancellationToken cancellationToken = default)
        {
            OnDelete?.Invoke(imageUrl);

            if (FailForUrl is not null && string.Equals(FailForUrl, imageUrl, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Cloudinary unavailable");
            }

            DeletedUrls.Add(imageUrl);
            return Task.CompletedTask;
        }
    }
}
