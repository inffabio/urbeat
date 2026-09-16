using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FluentAssertions;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Domain.Services;
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
            FreeShippingTodayDate = StoreOpeningHoursCalculator.GetSaoPauloDate(DateTimeOffset.UtcNow),
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
    public async Task GetPublicByIdAsync_ShouldReturnFreeShippingTodayFalse_WhenDateMissing()
    {
        var store = new Store
        {
            Name = "Loja Sem Data",
            Slug = "loja-sem-data",
            IsSubscriptionBlocked = false,
            FreeShippingToday = true,
            FreeShippingTodayDate = null
        };
        _db.Stores.Add(store);
        await _db.SaveChangesAsync();

        var result = await _sut.GetPublicByIdAsync(store.Id);

        result.Should().NotBeNull();
        result!.FreeShippingToday.Should().BeFalse();
    }

    [Fact]
    public async Task GetPublicByIdAsync_ShouldReturnFreeShippingTodayFalse_WhenDateExpired()
    {
        var store = new Store
        {
            Name = "Loja Data Expirada",
            Slug = "loja-data-expirada",
            IsSubscriptionBlocked = false,
            FreeShippingToday = true,
            FreeShippingTodayDate = StoreOpeningHoursCalculator.GetSaoPauloDate(DateTimeOffset.UtcNow).AddDays(-1)
        };
        _db.Stores.Add(store);
        await _db.SaveChangesAsync();

        var result = await _sut.GetPublicByIdAsync(store.Id);

        result.Should().NotBeNull();
        result!.FreeShippingToday.Should().BeFalse();
    }

    [Fact]
    public async Task GetByOwnerAsync_ShouldExposeEffectiveFreeShippingToday()
    {
        var ownerUserId = Guid.NewGuid();
        var store = new Store
        {
            OwnerUserId = ownerUserId,
            Name = "Loja Hoje",
            Slug = "loja-hoje",
            IsSubscriptionBlocked = false,
            FreeShippingToday = true,
            FreeShippingTodayDate = StoreOpeningHoursCalculator.GetSaoPauloDate(DateTimeOffset.UtcNow)
        };
        _db.Stores.Add(store);
        await _db.SaveChangesAsync();

        var result = await _sut.GetByOwnerAsync(ownerUserId);

        result.Should().NotBeNull();
        result!.FreeShippingToday.Should().BeTrue();
    }

    [Fact]
    public async Task GetByOwnerAsync_ShouldReturnFreeShippingTodayFalse_WhenDateExpired()
    {
        var ownerUserId = Guid.NewGuid();
        var store = new Store
        {
            OwnerUserId = ownerUserId,
            Name = "Loja Ontem",
            Slug = "loja-ontem",
            IsSubscriptionBlocked = false,
            FreeShippingToday = true,
            FreeShippingTodayDate = StoreOpeningHoursCalculator.GetSaoPauloDate(DateTimeOffset.UtcNow).AddDays(-1)
        };
        _db.Stores.Add(store);
        await _db.SaveChangesAsync();

        var result = await _sut.GetByOwnerAsync(ownerUserId);

        result.Should().NotBeNull();
        result!.FreeShippingToday.Should().BeFalse();
    }

    [Fact]
    public async Task GetByOwnerAsync_ShouldReturnIsPublishedFalse_WhenStoreWasNeverPublished()
    {
        var ownerUserId = Guid.NewGuid();
        var store = new Store
        {
            OwnerUserId = ownerUserId,
            Name = "Loja Nova",
            Slug = "loja-nova",
            IsSubscriptionBlocked = false,
            IsOpen = true
        };
        _db.Stores.Add(store);
        await _db.SaveChangesAsync();

        var result = await _sut.GetByOwnerAsync(ownerUserId);

        result.Should().NotBeNull();
        result!.IsPublished.Should().BeFalse();
        result.IsOpen.Should().BeTrue();
    }

    [Fact]
    public async Task GetByOwnerAsync_ShouldReturnIsPublishedTrue_WhenStoreWasPublished()
    {
        var ownerUserId = Guid.NewGuid();
        var store = new Store
        {
            OwnerUserId = ownerUserId,
            Name = "Loja Publicada",
            Slug = "loja-publicada",
            IsSubscriptionBlocked = false,
            IsPublished = true
        };
        _db.Stores.Add(store);
        await _db.SaveChangesAsync();

        var result = await _sut.GetByOwnerAsync(ownerUserId);

        result.Should().NotBeNull();
        result!.IsPublished.Should().BeTrue();
    }

    [Fact]
    public async Task GetPublicByIdAsync_ActiveFreeShippingTodayDate_ShouldReturnTrue()
    {
        var store = await SeedStoreAsync();
        store.FreeShippingToday = true;
        store.FreeShippingTodayDate = StoreOpeningHoursCalculator.GetSaoPauloDate(DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync();

        var result = await _sut.GetPublicByIdAsync(store.Id);

        result.Should().NotBeNull();
        result!.FreeShippingToday.Should().BeTrue();
    }

    [Fact]
    public async Task GetPublicByIdAsync_ExpiredFreeShippingTodayDate_ShouldReturnFalse()
    {
        var store = await SeedStoreAsync();
        store.FreeShippingToday = true;
        store.FreeShippingTodayDate = StoreOpeningHoursCalculator.GetSaoPauloDate(DateTimeOffset.UtcNow).AddDays(-1);
        await _db.SaveChangesAsync();

        var result = await _sut.GetPublicByIdAsync(store.Id);

        result.Should().NotBeNull();
        result!.FreeShippingToday.Should().BeFalse();
    }

    [Fact]
    public async Task GetPublicByIdAsync_MissingFreeShippingTodayDate_ShouldReturnFalse()
    {
        var store = await SeedStoreAsync();
        store.FreeShippingToday = true;
        store.FreeShippingTodayDate = null;
        await _db.SaveChangesAsync();

        var result = await _sut.GetPublicByIdAsync(store.Id);

        result.Should().NotBeNull();
        result!.FreeShippingToday.Should().BeFalse();
    }

    [Fact]
    public async Task GetPublicBySlugAsync_ExpiredFreeShippingTodayDate_ShouldReturnFalse()
    {
        var store = await SeedStoreAsync();
        store.FreeShippingToday = true;
        store.FreeShippingTodayDate = StoreOpeningHoursCalculator.GetSaoPauloDate(DateTimeOffset.UtcNow).AddDays(-1);
        await _db.SaveChangesAsync();

        var result = await _sut.GetPublicBySlugAsync(store.Slug);

        result.Should().NotBeNull();
        result!.FreeShippingToday.Should().BeFalse();
    }

    [Fact]
    public async Task GetByOwnerAsync_ActiveFreeShippingTodayDate_ShouldReturnTrue()
    {
        var ownerId = Guid.NewGuid();
        var store = new Store
        {
            OwnerUserId = ownerId,
            Name = "Loja Seller Ativa",
            Slug = "loja-seller-ativa",
            FreeShippingToday = true,
            FreeShippingTodayDate = StoreOpeningHoursCalculator.GetSaoPauloDate(DateTimeOffset.UtcNow)
        };
        _db.Stores.Add(store);
        await _db.SaveChangesAsync();

        var result = await _sut.GetByOwnerAsync(ownerId);

        result.Should().NotBeNull();
        result!.FreeShippingToday.Should().BeTrue();
    }

    [Fact]
    public async Task GetByOwnerAsync_ExpiredFreeShippingTodayDate_ShouldReturnFalse()
    {
        var ownerId = Guid.NewGuid();
        var store = new Store
        {
            OwnerUserId = ownerId,
            Name = "Loja Seller Expirada",
            Slug = "loja-seller-expirada",
            FreeShippingToday = true,
            FreeShippingTodayDate = StoreOpeningHoursCalculator.GetSaoPauloDate(DateTimeOffset.UtcNow).AddDays(-1)
        };
        _db.Stores.Add(store);
        await _db.SaveChangesAsync();

        var result = await _sut.GetByOwnerAsync(ownerId);

        result.Should().NotBeNull();
        result!.FreeShippingToday.Should().BeFalse();
    }

    [Fact]
    public async Task GetByOwnerAsync_MissingFreeShippingTodayDate_ShouldReturnFalse()
    {
        var ownerId = Guid.NewGuid();
        var store = new Store
        {
            OwnerUserId = ownerId,
            Name = "Loja Seller Sem Data",
            Slug = "loja-seller-sem-data",
            FreeShippingToday = true,
            FreeShippingTodayDate = null
        };
        _db.Stores.Add(store);
        await _db.SaveChangesAsync();

        var result = await _sut.GetByOwnerAsync(ownerId);

        result.Should().NotBeNull();
        result!.FreeShippingToday.Should().BeFalse();
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
    public async Task ListPublicAsync_ShouldExposeEffectiveFreeShippingToday_WhenActiveToday()
    {
        var store = await SeedStoreAsync();

        var results = await _sut.ListPublicAsync(cuisineType: null);

        var item = results.Single(x => x.Id == store.Id);
        item.FreeShippingToday.Should().BeTrue();
    }

    [Fact]
    public async Task ListPublicAsync_ShouldReturnFreeShippingTodayFalse_WhenDateExpired()
    {
        var store = await SeedStoreAsync();
        store.FreeShippingToday = true;
        store.FreeShippingTodayDate = StoreOpeningHoursCalculator.GetSaoPauloDate(DateTimeOffset.UtcNow).AddDays(-1);
        await _db.SaveChangesAsync();

        var results = await _sut.ListPublicAsync(cuisineType: null);

        results.Single(x => x.Id == store.Id).FreeShippingToday.Should().BeFalse();
    }

    [Fact]
    public async Task ListPublicAsync_ShouldReturnFreeShippingTodayFalse_WhenDateMissing()
    {
        var store = await SeedStoreAsync();
        store.FreeShippingToday = true;
        store.FreeShippingTodayDate = null;
        await _db.SaveChangesAsync();

        var results = await _sut.ListPublicAsync(cuisineType: null);

        results.Single(x => x.Id == store.Id).FreeShippingToday.Should().BeFalse();
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

    [Fact]
    public void DapperPublicStoreQueries_ShouldProjectSlugExactlyOnce()
    {
        var source = File.ReadAllText(ResolveRepositorySourcePath());

        var duplicateSlugProjection = new Regex(
            "\"Slug\",\\s*\\r?\\n\\s*\"Slug\",|s\\.\"Slug\",\\s*\\r?\\n\\s*s\\.\"Slug\",");

        duplicateSlugProjection.IsMatch(source).Should().BeFalse(
            "the public storefront Dapper queries must not project Slug twice");
    }

    private static string ResolveRepositorySourcePath([CallerFilePath] string callerFilePath = "")
    {
        var testDirectory = Path.GetDirectoryName(callerFilePath)!;
        var repositoryRoot = Path.GetFullPath(Path.Combine(testDirectory, "..", "..", "..", "..", ".."));

        return Path.Combine(
            repositoryRoot,
            "backend",
            "src",
            "Urbeat.Infrastructure",
            "Persistence",
            "ReadRepositories",
            "StoreReadRepository.cs");
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
