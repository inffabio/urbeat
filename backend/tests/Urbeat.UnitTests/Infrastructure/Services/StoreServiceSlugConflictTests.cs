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
using Npgsql;

namespace Urbeat.UnitTests.Infrastructure.Services;

public sealed class StoreServiceSlugConflictTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly StoreService _sut;

    public StoreServiceSlugConflictTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-store-slug-conflict-{Guid.NewGuid()}")
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

    private static StoreService CreateSut(ApplicationDbContext db, IEfUnitOfWork unitOfWork)
    {
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<EntityToDtoProfile>()).CreateMapper();
        return new StoreService(
            db,
            mapper,
            Mock.Of<IStoreReadRepository>(),
            unitOfWork,
            Mock.Of<IImageUploadService>());
    }

    private static DbUpdateException SlugUniqueIndexRaceViolation()
    {
        return new DbUpdateException(
            "save failed",
            new PostgresException(
                "duplicate key value violates unique constraint \"IX_Stores_Slug\"",
                "ERROR",
                "ERROR",
                PostgresErrorCodes.UniqueViolation));
    }

    private async Task<CuisineType> SeedActiveCuisineAsync(string name = "Pizzaria")
    {
        var cuisine = new CuisineType { Name = name, IsActive = true, IsDefault = true, StoreId = null };
        _db.CuisineTypes.Add(cuisine);
        await _db.SaveChangesAsync();
        return cuisine;
    }

    [Fact]
    public async Task CreateForOwnerAsync_ShouldReportSlugConflict_WhenRequestedSlugAlreadyExists()
    {
        _db.Stores.Add(new Store
        {
            OwnerUserId = Guid.NewGuid(),
            Name = "Loja Existente",
            Slug = "minha-loja",
            PhoneNumber = "11999999999"
        });
        await SeedActiveCuisineAsync();
        await _db.SaveChangesAsync();

        var result = await _sut.CreateForOwnerAsync(
            Guid.NewGuid(),
            new CreateStoreRequestDto
            {
                Name = "Minha Loja",
                Slug = "minha-loja",
                PhoneNumber = "11888888888",
                CuisineType = "Pizzaria"
            },
            ipAddress: null);

        result.SlugConflict.Should().BeTrue();
        result.Created.Should().BeFalse();
        result.Store.Should().BeNull();
        _db.Stores.Should().HaveCount(1);
        _db.Stores.Single().Slug.Should().Be("minha-loja");
    }

    [Fact]
    public async Task CreateForOwnerAsync_ShouldReportSlugConflict_WhenDerivedSlugFromNameAlreadyExists()
    {
        _db.Stores.Add(new Store
        {
            OwnerUserId = Guid.NewGuid(),
            Name = "Loja Existente",
            Slug = "minha-loja",
            PhoneNumber = "11999999999"
        });
        await SeedActiveCuisineAsync();
        await _db.SaveChangesAsync();

        var result = await _sut.CreateForOwnerAsync(
            Guid.NewGuid(),
            new CreateStoreRequestDto
            {
                Name = "Minha Loja",
                Slug = string.Empty,
                PhoneNumber = "11888888888",
                CuisineType = "Pizzaria"
            },
            ipAddress: null);

        result.SlugConflict.Should().BeTrue();
        result.Created.Should().BeFalse();
        _db.Stores.Should().HaveCount(1);
        _db.Stores.Single().Slug.Should().Be("minha-loja");
    }

    [Fact]
    public async Task UpdateAsync_ShouldReportSlugConflict_WhenRequestedSlugBelongsToAnotherStore()
    {
        var ownerUserId = Guid.NewGuid();
        var store = new Store
        {
            OwnerUserId = ownerUserId,
            Name = "Loja A",
            Slug = "loja-a",
            PhoneNumber = "11999999999"
        };
        _db.Stores.Add(store);
        _db.Stores.Add(new Store
        {
            OwnerUserId = Guid.NewGuid(),
            Name = "Loja B",
            Slug = "loja-b",
            PhoneNumber = "11888888888"
        });
        await SeedActiveCuisineAsync();
        await _db.SaveChangesAsync();

        var result = await _sut.UpdateAsync(
            ownerUserId,
            store.Id,
            new UpdateStoreRequestDto
            {
                Name = "Loja A",
                Slug = "loja-b",
                PhoneNumber = "11999999999",
                CuisineType = "Pizzaria"
            },
            ipAddress: null);

        result.SlugConflict.Should().BeTrue();
        result.Store.Should().BeNull();

        var reloaded = await _db.Stores.SingleAsync(x => x.Id == store.Id);
        reloaded.Slug.Should().Be("loja-a");
        reloaded.Name.Should().Be("Loja A");
    }

    [Fact]
    public async Task UpdateAsync_ShouldPreserveOwnSlug_WhenRequestedSlugBelongsToTheSameStore()
    {
        var ownerUserId = Guid.NewGuid();
        var store = new Store
        {
            OwnerUserId = ownerUserId,
            Name = "Loja A",
            Slug = "loja-a",
            PhoneNumber = "11999999999"
        };
        _db.Stores.Add(store);
        _db.Stores.Add(new Store
        {
            OwnerUserId = Guid.NewGuid(),
            Name = "Loja B",
            Slug = "loja-b",
            PhoneNumber = "11888888888"
        });
        await SeedActiveCuisineAsync();
        await _db.SaveChangesAsync();

        var result = await _sut.UpdateAsync(
            ownerUserId,
            store.Id,
            new UpdateStoreRequestDto
            {
                Name = "Loja A Renomeada",
                Slug = "loja-a",
                PhoneNumber = "11999999999",
                CuisineType = "Pizzaria"
            },
            ipAddress: null);

        result.SlugConflict.Should().BeFalse();
        result.Store.Should().NotBeNull();
        result.Store!.Slug.Should().Be("loja-a");

        var reloaded = await _db.Stores.SingleAsync(x => x.Id == store.Id);
        reloaded.Slug.Should().Be("loja-a");
        reloaded.Name.Should().Be("Loja A Renomeada");
    }

    // The pre-check is not the real guard: a concurrent request may persist the same slug between
    // the AnyAsync pre-check and SaveChanges. InMemory does not enforce the unique index, so these
    // tests simulate the losing save raising a Postgres unique_violation on the slug index.
    [Fact]
    public async Task CreateForOwnerAsync_ShouldReportSlugConflict_WhenSaveFailsOnSlugUniqueIndexRace()
    {
        await SeedActiveCuisineAsync();

        var uow = new Mock<IEfUnitOfWork>();
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(SlugUniqueIndexRaceViolation());
        var sut = CreateSut(_db, uow.Object);

        var result = await sut.CreateForOwnerAsync(
            Guid.NewGuid(),
            new CreateStoreRequestDto
            {
                Name = "Minha Loja",
                Slug = "minha-loja",
                PhoneNumber = "11888888888",
                CuisineType = "Pizzaria"
            },
            ipAddress: null);

        result.SlugConflict.Should().BeTrue();
        result.Created.Should().BeFalse();
        result.AlreadyExists.Should().BeFalse();
        result.Store.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_ShouldReportSlugConflict_WhenSaveFailsOnSlugUniqueIndexRace()
    {
        var ownerUserId = Guid.NewGuid();
        _db.Stores.Add(new Store
        {
            OwnerUserId = ownerUserId,
            Name = "Loja A",
            Slug = "loja-a",
            PhoneNumber = "11999999999"
        });
        await SeedActiveCuisineAsync();
        await _db.SaveChangesAsync();

        var uow = new Mock<IEfUnitOfWork>();
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(SlugUniqueIndexRaceViolation());
        var sut = CreateSut(_db, uow.Object);

        var result = await sut.UpdateAsync(
            ownerUserId,
            _db.Stores.Single(x => x.Slug == "loja-a").Id,
            new UpdateStoreRequestDto
            {
                Name = "Loja A",
                Slug = "loja-b",
                PhoneNumber = "11999999999",
                CuisineType = "Pizzaria"
            },
            ipAddress: null);

        result.SlugConflict.Should().BeTrue();
        result.Store.Should().BeNull();
    }

    [Fact]
    public async Task CreateForOwnerAsync_ShouldNotMaskNonSlugUniqueViolation()
    {
        await SeedActiveCuisineAsync();

        // A different Stores unique index (e.g. OwnerUserId) must not be reported as a slug conflict.
        var uow = new Mock<IEfUnitOfWork>();
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException(
                "save failed",
                new PostgresException(
                    "duplicate key value violates unique constraint \"IX_Stores_OwnerUserId\"",
                    "ERROR",
                    "ERROR",
                    PostgresErrorCodes.UniqueViolation)));
        var sut = CreateSut(_db, uow.Object);

        var act = () => sut.CreateForOwnerAsync(
            Guid.NewGuid(),
            new CreateStoreRequestDto
            {
                Name = "Minha Loja",
                Slug = "minha-loja",
                PhoneNumber = "11888888888",
                CuisineType = "Pizzaria"
            },
            ipAddress: null);

        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
