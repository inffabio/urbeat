using FluentAssertions;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Persistence.ReadRepositories;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Urbeat.UnitTests.Infrastructure.Persistence;

public sealed class StoreReadRepositoryTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly StoreReadRepository _sut;

    public StoreReadRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-srr-{Guid.NewGuid()}")
            .Options;
        _db = new ApplicationDbContext(options);

        var dapperMock = new Mock<IDapperUnitOfWork>();
        _sut = new StoreReadRepository(_db, dapperMock.Object);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private async Task<Store> SeedStoreAsync(bool withAddress = true, bool withHours = true)
    {
        var store = new Store
        {
            Name = "Loja Teste",
            Slug = "loja-teste",
            PhoneNumber = "11999999999",
            Description = "Descricao da loja",
            IsOpen = true,
            IsSubscriptionBlocked = false,
            SupportsDelivery = true,
            SupportsPickup = true,
            InitialMinute = 30,
            FinalMinute = 60,
            DeliveryFee = 5.00m,
            MinimumOrderValue = 20.00m,
            FreeShippingThreshold = 50.00m,
            FreeShippingToday = true,
            AverageRating = 4.5,
            TotalReviews = 42,
            MaxDeliveryRadiusKm = 10,
            BannerUrl = "https://example.com/banner.jpg",
            LogoUrl = "https://example.com/logo.jpg",
            CuisineType = new CuisineType { Name = "Pizza" }
        };

        _db.Stores.Add(store);
        await _db.SaveChangesAsync();

        if (withAddress)
        {
            var addr = new StoreAddress
            {
                StoreId = store.Id,
                Street = "Rua A",
                Number = "100",
                Neighborhood = "Centro",
                City = "Sao Paulo",
                State = "SP",
                ZipCode = "01001000"
            };
            _db.StoreAddresses.Add(addr);
        }

        if (withHours)
        {
            var hours = new StoreBusinessHour
            {
                StoreId = store.Id,
                DayOfWeek = DayOfWeek.Monday,
                IsOpen = true,
                
            };
            _db.StoreBusinessHours.Add(hours);
        }

        await _db.SaveChangesAsync();
        return store;
    }

    private async Task<Store> SeedStoreBlockedAsync()
    {
        var blocked = new Store
        {
            Name = "Loja Bloqueada",
            Slug = "loja-bloqueada",
            IsSubscriptionBlocked = true
        };
        _db.Stores.Add(blocked);
        await _db.SaveChangesAsync();
        return blocked;
    }

    [Fact]
    public async Task GetPublicByIdAsync_ShouldReturnAllCoreFields()
    {
        var store = await SeedStoreAsync();

        var result = await _sut.GetPublicByIdAsync(store.Id);

        result.Should().NotBeNull();
        result!.FreeShippingThreshold.Should().Be(50.00m);
        result.FreeShippingToday.Should().BeTrue();
        result.SupportsDelivery.Should().BeTrue();
        result.SupportsPickup.Should().BeTrue();
        result.InitialMinute.Should().Be(30);
        result.FinalMinute.Should().Be(60);
    }

    [Fact]
    public async Task GetPublicBySlugAsync_ShouldReturnAllCoreFields()
    {
        var store = await SeedStoreAsync();

        var result = await _sut.GetPublicBySlugAsync(store.Slug);

        result.Should().NotBeNull();
        result!.FreeShippingThreshold.Should().Be(50.00m);
        result.FreeShippingToday.Should().BeTrue();
        result.SupportsDelivery.Should().BeTrue();
        result.SupportsPickup.Should().BeTrue();
        result.InitialMinute.Should().Be(30);
        result.FinalMinute.Should().Be(60);
    }

    [Fact]
    public async Task GetPublicByPathAsync_ShouldReturnAllCoreFields()
    {
        var store = await SeedStoreAsync();

        var result = await _sut.GetPublicByPathAsync(store.Slug);

        result.Should().NotBeNull();
        result!.FreeShippingThreshold.Should().Be(50.00m);
        result.FreeShippingToday.Should().BeTrue();
        result.SupportsDelivery.Should().BeTrue();
        result.SupportsPickup.Should().BeTrue();
        result.InitialMinute.Should().Be(30);
        result.FinalMinute.Should().Be(60);
    }

    [Fact]
    public async Task GetPublicByIdAsync_ShouldReturnAddress()
    {
        var store = await SeedStoreAsync(withAddress: true);

        var result = await _sut.GetPublicByIdAsync(store.Id);

        result.Should().NotBeNull();
        result!.Address.Should().NotBeNull();
        result.Address!.Street.Should().Be("Rua A");
        result.Address.City.Should().Be("Sao Paulo");
    }

    [Fact]
    public async Task GetPublicBySlugAsync_ShouldReturnAddressAndHours()
    {
        var store = await SeedStoreAsync(withAddress: true, withHours: true);

        var result = await _sut.GetPublicBySlugAsync(store.Slug);

        result.Should().NotBeNull();
        result!.Address.Should().NotBeNull();
        result.BusinessHours.Should().HaveCount(1);
        result.BusinessHours.First().DayOfWeek.Should().Be(DayOfWeek.Monday);
    }

    [Fact]
    public async Task GetPublicByPathAsync_ShouldReturnAddressAndHours()
    {
        var store = await SeedStoreAsync(withAddress: true, withHours: true);

        var result = await _sut.GetPublicByPathAsync(store.Slug);

        result.Should().NotBeNull();
        result!.Address.Should().NotBeNull();
        result.BusinessHours.Should().HaveCount(1);
    }

    [Fact]
    public async Task ListPublicAsync_ShouldReturnFreeShippingThreshold()
    {
        var store = await SeedStoreAsync();

        var results = await _sut.ListPublicAsync(cuisineType: null);

        results.Should().NotBeEmpty();
        results.Should().ContainSingle(x => x.Id == store.Id && x.FreeShippingThreshold == 50.00m);
    }

    [Fact]
    public async Task GetPublicByIdAsync_NotFound_ShouldReturnNull()
    {
        var result = await _sut.GetPublicByIdAsync(Guid.NewGuid());
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetPublicBySlugAsync_NotFound_ShouldReturnNull()
    {
        var result = await _sut.GetPublicBySlugAsync("slug-inexistente");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetPublicByPathAsync_NotFound_ShouldReturnNull()
    {
        var result = await _sut.GetPublicByPathAsync("path-inexistente");
        result.Should().BeNull();
    }

    [Fact]
    public async Task ListPublicAsync_BlockedStore_ShouldBeExcluded()
    {
        var blocked = await SeedStoreBlockedAsync();

        var results = await _sut.ListPublicAsync(cuisineType: null);

        results.Should().NotContain(x => x.Id == blocked.Id);
    }

    [Fact]
    public async Task GetPublicByIdAsync_IsOpenNow_ShouldBeComputedFromBusinessHours()
    {
        var store = new Store
        {
            Name = "Loja Aberta",
            Slug = "loja-aberta",
            IsOpen = true,
            IsSubscriptionBlocked = false
        };
        _db.Stores.Add(store);
        await _db.SaveChangesAsync();

        _db.StoreBusinessHours.Add(new StoreBusinessHour
        {
            StoreId = store.Id,
            DayOfWeek = CurrentSaoPauloDayOfWeek(),
            IsOpen = true,
            Shifts = [new StoreBusinessHourShift
            {
                StartTime = new TimeOnly(0, 0),
                EndTime = new TimeOnly(23, 59)
            }]
        });
        await _db.SaveChangesAsync();

        var result = await _sut.GetPublicByIdAsync(store.Id);

        result.Should().NotBeNull();
        result!.IsOpenNow.Should().BeTrue();
    }

    [Fact]
    public async Task GetPublicBySlugAsync_IsOpenNow_ShouldBeComputedFromBusinessHours()
    {
        var store = new Store
        {
            Name = "Loja Fechada",
            Slug = "loja-fechada",
            IsOpen = true,
            IsSubscriptionBlocked = false
        };
        _db.Stores.Add(store);
        await _db.SaveChangesAsync();

        _db.StoreBusinessHours.Add(new StoreBusinessHour
        {
            StoreId = store.Id,
            DayOfWeek = CurrentSaoPauloDayOfWeek(),
            IsOpen = true,
            Shifts = [new StoreBusinessHourShift
            {
                StartTime = new TimeOnly(0, 0),
                EndTime = new TimeOnly(23, 59)
            }]
        });
        await _db.SaveChangesAsync();

        var result = await _sut.GetPublicBySlugAsync(store.Slug);

        result.Should().NotBeNull();
        result!.IsOpenNow.Should().BeTrue();
    }

    [Fact]
    public async Task GetPublicByPathAsync_IsOpenNow_ShouldBeComputedFromBusinessHours()
    {
        var store = new Store
        {
            Name = "Loja Fechada2",
            Slug = "loja-fechada2",
            IsOpen = true,
            IsSubscriptionBlocked = false
        };
        _db.Stores.Add(store);
        await _db.SaveChangesAsync();

        _db.StoreBusinessHours.Add(new StoreBusinessHour
        {
            StoreId = store.Id,
            DayOfWeek = CurrentSaoPauloDayOfWeek(),
            IsOpen = true,
            Shifts = [new StoreBusinessHourShift
            {
                StartTime = new TimeOnly(0, 0),
                EndTime = new TimeOnly(23, 59)
            }]
        });
        await _db.SaveChangesAsync();

        var result = await _sut.GetPublicByPathAsync(store.Slug);

        result.Should().NotBeNull();
        result!.IsOpenNow.Should().BeTrue();
    }

    private static DayOfWeek CurrentSaoPauloDayOfWeek()
    {
        TimeZoneInfo timeZone;
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        }
        catch (TimeZoneNotFoundException)
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");
        }

        return TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone).DayOfWeek;
    }
}
