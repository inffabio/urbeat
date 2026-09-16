using AutoMapper;
using FluentAssertions;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Persistence.UnitOfWork;
using Urbeat.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Urbeat.UnitTests.Infrastructure.Services;

public sealed class StoreServiceDeliveryTimesTests
{
    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-dt-tests-{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static StoreService CreateSut(ApplicationDbContext db)
    {
        return new StoreService(
            db,
            new Mock<IMapper>().Object,
            new Mock<IStoreReadRepository>().Object,
            new EfUnitOfWork(db),
            new Mock<IImageUploadService>().Object);
    }

    private static Store NewStore(Guid ownerUserId) => new()
    {
        OwnerUserId = ownerUserId,
        Name = "Loja Teste",
        Slug = $"loja-teste-{Guid.NewGuid():N}",
        PhoneNumber = "11999999999"
    };

    [Fact]
    public async Task CreateDeliveryTimeAsync_ShouldReturnForbidden_WhenStoreBelongsToAnotherSeller()
    {
        using var db = CreateDbContext();
        var store = NewStore(Guid.NewGuid());
        db.Stores.Add(store);
        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        var result = await sut.CreateDeliveryTimeAsync(Guid.NewGuid(), store.Id, 10, 20);

        result.Forbidden.Should().BeTrue();
        result.DeliveryTime.Should().BeNull();
        (await db.Set<DeliveryTime>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateDeliveryTimeAsync_ShouldReturnNotFound_WhenStoreDoesNotExist()
    {
        using var db = CreateDbContext();
        var sut = CreateSut(db);

        var result = await sut.CreateDeliveryTimeAsync(Guid.NewGuid(), Guid.NewGuid(), 10, 20);

        result.NotFound.Should().BeTrue();
        result.DeliveryTime.Should().BeNull();
    }

    [Fact]
    public async Task CreateDeliveryTimeAsync_ShouldCreate_WhenOwnerMatches()
    {
        using var db = CreateDbContext();
        var owner = Guid.NewGuid();
        var store = NewStore(owner);
        db.Stores.Add(store);
        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        var result = await sut.CreateDeliveryTimeAsync(owner, store.Id, 10, 20);

        result.Forbidden.Should().BeFalse();
        result.DeliveryTime.Should().NotBeNull();
        result.DeliveryTime!.MinTimeMinutes.Should().Be(10);
        (await db.Set<DeliveryTime>().CountAsync()).Should().Be(1);
    }
}
