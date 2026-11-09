using AutoMapper;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Mappings;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Persistence.UnitOfWork;
using Urbeat.Infrastructure.Services;

namespace Urbeat.UnitTests.Infrastructure.Services;

/// <summary>
/// Stores.CuisineTypeId -> CuisineTypes and CuisineTypes.StoreId -> Stores are two independent
/// foreign keys. When a brand-new store and its new private cuisine type are both inserted in
/// the same SaveChanges, EF cannot order the batch and throws a circular-dependency exception
/// before executing any SQL. The InMemory provider does not run the relational batching, so this
/// test records every SaveChanges batch to pin the two-phase write.
/// </summary>
public sealed class StoreServiceCreateBatchingTests : IDisposable
{
    private sealed record TrackedEntry(string Entity, EntityState State);

    private sealed class BatchRecordingUnitOfWork : IEfUnitOfWork
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly EfUnitOfWork _inner;

        public BatchRecordingUnitOfWork(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
            _inner = new EfUnitOfWork(dbContext);
        }

        public List<IReadOnlyList<TrackedEntry>> Batches { get; } = new();

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Batches.Add(_dbContext.ChangeTracker.Entries()
                .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .Select(entry => new TrackedEntry(entry.Metadata.ClrType.Name, entry.State))
                .ToList());

            return _inner.SaveChangesAsync(cancellationToken);
        }
    }

    private readonly ApplicationDbContext _db;
    private readonly BatchRecordingUnitOfWork _unitOfWork;
    private readonly StoreService _sut;

    public StoreServiceCreateBatchingTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-store-create-batching-{Guid.NewGuid()}")
            .Options;
        _db = new ApplicationDbContext(options);
        _unitOfWork = new BatchRecordingUnitOfWork(_db);

        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<EntityToDtoProfile>()).CreateMapper();
        _sut = new StoreService(
            _db,
            mapper,
            Mock.Of<IStoreReadRepository>(),
            _unitOfWork,
            Mock.Of<IImageUploadService>());
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task CreateForOwnerAsync_ShouldPersistStoreAndPrivateCuisine_InSeparateBatches()
    {
        var ownerUserId = Guid.NewGuid();

        // Payload produced by the store wizard when the seller adds a category before the
        // store exists: the pending category name is sent as the store's cuisine type.
        var result = await _sut.CreateForOwnerAsync(ownerUserId, new CreateStoreRequestDto
        {
            Name = "Loja Categoria Nova",
            Slug = "loja-categoria-nova",
            PhoneNumber = "11999999999",
            CuisineType = "Comida Vegana",
            SupportsDelivery = true,
            SupportsPickup = true,
            InitialMinute = 600,
            FinalMinute = 1380,
            MaxDeliveryRadiusKm = 5
        }, ipAddress: null);

        result.Created.Should().BeTrue();

        var storeAndCuisineInsertedTogether = _unitOfWork.Batches.Any(batch =>
            batch.Any(entry => entry is { Entity: nameof(Store), State: EntityState.Added })
            && batch.Any(entry => entry is { Entity: nameof(CuisineType), State: EntityState.Added }));

        storeAndCuisineInsertedTogether.Should().BeFalse(
            "the store and its private cuisine type have mutually dependent foreign keys and cannot share an INSERT batch");

        var store = await _db.Stores.AsNoTracking().SingleAsync(x => x.OwnerUserId == ownerUserId);
        var privateCuisine = await _db.CuisineTypes.AsNoTracking().SingleAsync(x => x.StoreId == store.Id);

        privateCuisine.Name.Should().Be("Comida Vegana");
        privateCuisine.NormalizedName.Should().Be("comida vegana");
        privateCuisine.IsDefault.Should().BeFalse();
        store.CuisineTypeId.Should().Be(privateCuisine.Id);
        (await _db.CuisineTypes.AsNoTracking().CountAsync(x => x.NormalizedName == "comida vegana")).Should().Be(1);
    }

    [Fact]
    public async Task CreateForOwnerAsync_ShouldPersistGlobalCuisine_InASingleStoreInsert()
    {
        var global = new CuisineType { Name = "Pizzaria", IsDefault = true, IsActive = true, StoreId = null };
        _db.CuisineTypes.Add(global);
        await _db.SaveChangesAsync();
        _unitOfWork.Batches.Clear();

        var ownerUserId = Guid.NewGuid();
        var result = await _sut.CreateForOwnerAsync(ownerUserId, new CreateStoreRequestDto
        {
            Name = "Loja Global",
            Slug = "loja-global",
            PhoneNumber = "11999999999",
            CuisineType = "pizzaria"
        }, ipAddress: null);

        result.Created.Should().BeTrue();
        _unitOfWork.Batches
            .Count(batch => batch.Any(entry => entry is { Entity: nameof(Store), State: EntityState.Added }))
            .Should().Be(1);

        var store = await _db.Stores.AsNoTracking().SingleAsync(x => x.OwnerUserId == ownerUserId);
        store.CuisineTypeId.Should().Be(global.Id);
        (await _db.CuisineTypes.AsNoTracking().CountAsync()).Should().Be(1);
    }
}
