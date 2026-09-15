using AutoMapper;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Domain.Services;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Urbeat.UnitTests.Infrastructure.Services;

public sealed class StoreServiceDeliveryNeighborhoodsTests
{
    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-dn-tests-{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static StoreService CreateSut(ApplicationDbContext db)
    {
        var mapperMock = new Mock<IMapper>();
        var storeReadRepoMock = new Mock<IStoreReadRepository>();
        var unitOfWorkMock = new Mock<IEfUnitOfWork>();
        var imageUploadMock = new Mock<IImageUploadService>();

        return new StoreService(
            db,
            mapperMock.Object,
            storeReadRepoMock.Object,
            unitOfWorkMock.Object,
            imageUploadMock.Object);
    }

    [Fact]
    public async Task GetActiveDeliveryNeighborhoodsByStoreAsync_ShouldReturnOnlyStoreCityNeighborhoods_WhenRadiusIsSet()
    {
        using var db = CreateDbContext();
        var storeCity = "Sao Paulo";

        var store = new Store
        {
            Name = "Loja Teste SP",
            Slug = "loja-teste-sp",
            MaxDeliveryRadiusKm = 10
        };
        db.Stores.Add(store);

        var storeAddress = new StoreAddress
        {
            StoreId = store.Id,
            City = storeCity,
            State = "SP",
            Latitude = -23.5505,
            Longitude = -46.6333
        };
        db.StoreAddresses.Add(storeAddress);

        var nbSameCity = new DeliveryNeighborhood
        {
            Neighborhood = "Pinheiros",
            NormalizedName = "pinheiros",
            City = storeCity,
            CityId = Guid.NewGuid(),
            Latitude = -23.5667,
            Longitude = -46.6833,
            IsActive = true,
            Source = "test"
        };
        db.DeliveryNeighborhoods.Add(nbSameCity);

        var nbOtherCity = new DeliveryNeighborhood
        {
            Neighborhood = "Copacabana",
            NormalizedName = "copacabana",
            City = "Rio de Janeiro",
            CityId = Guid.NewGuid(),
            Latitude = -22.9711,
            Longitude = -43.1822,
            IsActive = true,
            Source = "test"
        };
        db.DeliveryNeighborhoods.Add(nbOtherCity);

        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        var result = await sut.GetActiveDeliveryNeighborhoodsByStoreAsync(store.Id);

        result.Should().HaveCount(1);
        result.Single().Neighborhood.Should().Be("Pinheiros");
        result.Single().City.Should().Be(storeCity);
    }

    [Fact]
    public async Task GetActiveDeliveryNeighborhoodsByStoreAsync_ShouldExcludeNeighborhoodFromAnotherCityWithinRadius_WhenRadiusIsSet()
    {
        using var db = CreateDbContext();
        var storeCity = "Sao Paulo";

        var store = new Store
        {
            Name = "Loja SP",
            Slug = "loja-sp",
            MaxDeliveryRadiusKm = 10
        };
        db.Stores.Add(store);

        var storeAddress = new StoreAddress
        {
            StoreId = store.Id,
            City = storeCity,
            State = "SP",
            Latitude = -23.5505,
            Longitude = -46.6333
        };
        db.StoreAddresses.Add(storeAddress);

        var nbOtherCityWithinRadius = new DeliveryNeighborhood
        {
            Neighborhood = "Vila Guarulhos",
            NormalizedName = "vila guarulhos",
            City = "Guarulhos",
            CityId = Guid.NewGuid(),
            Latitude = -23.5540,
            Longitude = -46.6400,
            IsActive = true,
            Source = "test"
        };
        db.DeliveryNeighborhoods.Add(nbOtherCityWithinRadius);

        var nbSameCityWithinRadius = new DeliveryNeighborhood
        {
            Neighborhood = "Centro",
            NormalizedName = "centro",
            City = storeCity,
            CityId = Guid.NewGuid(),
            Latitude = -23.5580,
            Longitude = -46.6420,
            IsActive = true,
            Source = "test"
        };
        db.DeliveryNeighborhoods.Add(nbSameCityWithinRadius);

        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        var result = await sut.GetActiveDeliveryNeighborhoodsByStoreAsync(store.Id);

        result.Should().HaveCount(1);
        result.Single().Neighborhood.Should().Be("Centro");
    }

    [Fact]
    public async Task GetActiveDeliveryNeighborhoodsByStoreAsync_ShouldReturnEmpty_WhenStoreNotFound()
    {
        using var db = CreateDbContext();
        var sut = CreateSut(db);

        var result = await sut.GetActiveDeliveryNeighborhoodsByStoreAsync(Guid.NewGuid());

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetActiveDeliveryNeighborhoodsByStoreAsync_ShouldFallbackToCityName_WhenRadiusIsZero()
    {
        using var db = CreateDbContext();
        var storeCity = "Curitiba";

        var store = new Store
        {
            Name = "Loja Curitiba",
            Slug = "loja-curitiba",
            MaxDeliveryRadiusKm = null
        };
        db.Stores.Add(store);

        var storeAddress = new StoreAddress
        {
            StoreId = store.Id,
            City = storeCity,
            State = "PR"
        };
        db.StoreAddresses.Add(storeAddress);

        var nb = new DeliveryNeighborhood
        {
            Neighborhood = "Centro",
            NormalizedName = "centro",
            City = storeCity,
            IsActive = true,
            Source = "test"
        };
        db.DeliveryNeighborhoods.Add(nb);

        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        var result = await sut.GetActiveDeliveryNeighborhoodsByStoreAsync(store.Id);

        result.Should().HaveCount(1);
        result.Single().Neighborhood.Should().Be("Centro");
    }

    [Fact]
    public async Task GetActiveDeliveryNeighborhoodsByStoreAsync_ShouldFallbackToCityName_WhenAddressHasNoCoordinates()
    {
        using var db = CreateDbContext();
        var storeCity = "Belo Horizonte";

        var store = new Store
        {
            Name = "Loja BH",
            Slug = "loja-bh",
            MaxDeliveryRadiusKm = 10
        };
        db.Stores.Add(store);

        var storeAddress = new StoreAddress
        {
            StoreId = store.Id,
            City = storeCity,
            State = "MG",
            Latitude = null,
            Longitude = null
        };
        db.StoreAddresses.Add(storeAddress);

        var nb = new DeliveryNeighborhood
        {
            Neighborhood = "Savassi",
            NormalizedName = "savassi",
            City = storeCity,
            IsActive = true,
            Source = "test"
        };
        db.DeliveryNeighborhoods.Add(nb);

        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        var result = await sut.GetActiveDeliveryNeighborhoodsByStoreAsync(store.Id);

        result.Should().HaveCount(1);
        result.Single().Neighborhood.Should().Be("Savassi");
    }

    [Fact]
    public async Task GetActiveDeliveryNeighborhoodsByStoreAsync_ShouldFilterByHaversine_WhenRadiusAndCoordinatesAreSet()
    {
        using var db = CreateDbContext();
        var storeCity = "Sao Paulo";

        var store = new Store
        {
            Name = "Loja SP Centro",
            Slug = "loja-sp-centro",
            MaxDeliveryRadiusKm = 3
        };
        db.Stores.Add(store);

        var storeAddress = new StoreAddress
        {
            StoreId = store.Id,
            City = storeCity,
            State = "SP",
            Latitude = -23.5505,
            Longitude = -46.6333
        };
        db.StoreAddresses.Add(storeAddress);

        var nbNear = new DeliveryNeighborhood
        {
            Neighborhood = "Bela Vista",
            NormalizedName = "bela vista",
            City = storeCity,
            CityId = Guid.NewGuid(),
            Latitude = -23.5580,
            Longitude = -46.6420,
            IsActive = true,
            Source = "test"
        };
        db.DeliveryNeighborhoods.Add(nbNear);

        var nbFar = new DeliveryNeighborhood
        {
            Neighborhood = "Itaquera",
            NormalizedName = "itaquera",
            City = storeCity,
            CityId = Guid.NewGuid(),
            Latitude = -23.5400,
            Longitude = -46.4600,
            IsActive = true,
            Source = "test"
        };
        db.DeliveryNeighborhoods.Add(nbFar);

        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        var result = await sut.GetActiveDeliveryNeighborhoodsByStoreAsync(store.Id);

        result.Should().HaveCount(1);
        result.Single().Neighborhood.Should().Be("Bela Vista");
    }

    [Fact]
    public async Task GetActiveDeliveryNeighborhoodsByStoreAsync_ShouldReturnEmpty_WhenPositiveRadiusHasNoEligibleNeighborhoods()
    {
        using var db = CreateDbContext();
        var storeCity = "Sao Paulo";

        var store = new Store
        {
            Name = "Loja SP Zero",
            Slug = "loja-sp-zero",
            MaxDeliveryRadiusKm = 3
        };
        db.Stores.Add(store);

        var storeAddress = new StoreAddress
        {
            StoreId = store.Id,
            City = storeCity,
            State = "SP",
            Latitude = -23.5505,
            Longitude = -46.6333
        };
        db.StoreAddresses.Add(storeAddress);

        var nbFar = new DeliveryNeighborhood
        {
            Neighborhood = "Itaquera",
            NormalizedName = "itaquera",
            City = storeCity,
            CityId = Guid.NewGuid(),
            Latitude = -23.5400,
            Longitude = -46.4600,
            IsActive = true,
            Source = "test"
        };
        db.DeliveryNeighborhoods.Add(nbFar);

        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        var result = await sut.GetActiveDeliveryNeighborhoodsByStoreAsync(store.Id, radiusKm: 2);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetActiveDeliveryNeighborhoodsByStoreAsync_ShouldIncludeManualNeighborhoodWithoutCoordinates_WhenRadiusIsSet()
    {
        using var db = CreateDbContext();
        var storeCity = "Sao Paulo";

        var store = new Store
        {
            Name = "Loja SP",
            Slug = "loja-sp",
            MaxDeliveryRadiusKm = 10
        };
        db.Stores.Add(store);

        var storeAddress = new StoreAddress
        {
            StoreId = store.Id,
            City = storeCity,
            State = "SP",
            Latitude = -23.5505,
            Longitude = -46.6333
        };
        db.StoreAddresses.Add(storeAddress);

        var nbNoCoords = new DeliveryNeighborhood
        {
            Neighborhood = "Sem Coordenadas",
            NormalizedName = "sem coordenadas",
            City = storeCity,
            Latitude = null,
            Longitude = null,
            IsActive = true,
            Source = null
        };
        db.DeliveryNeighborhoods.Add(nbNoCoords);

        var nbWithCoords = new DeliveryNeighborhood
        {
            Neighborhood = "Com Coordenadas",
            NormalizedName = "com coordenadas",
            City = storeCity,
            Latitude = -23.5600,
            Longitude = -46.6400,
            IsActive = true,
            Source = "test"
        };
        db.DeliveryNeighborhoods.Add(nbWithCoords);

        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        var result = await sut.GetActiveDeliveryNeighborhoodsByStoreAsync(store.Id);

        result.Should().HaveCount(2);
        result.Should().Contain(x => x.Neighborhood == "Com Coordenadas");
        result.Should().Contain(x => x.Neighborhood == "Sem Coordenadas");
        result.Should().Contain(x => x.Neighborhood == "Sem Coordenadas" && x.Latitude == null && x.Longitude == null);
    }

    [Fact]
    public async Task GetActiveDeliveryNeighborhoodsByStoreAsync_ShouldNotReturnManualNeighborhoodFromAnotherCity_WhenRadiusIsSet()
    {
        using var db = CreateDbContext();
        var storeCity = "Sao Paulo";

        var store = new Store
        {
            Name = "Loja SP",
            Slug = "loja-sp",
            MaxDeliveryRadiusKm = 10
        };
        db.Stores.Add(store);

        var storeAddress = new StoreAddress
        {
            StoreId = store.Id,
            City = storeCity,
            State = "SP",
            Latitude = -23.5505,
            Longitude = -46.6333
        };
        db.StoreAddresses.Add(storeAddress);

        var nbManualStoreCity = new DeliveryNeighborhood
        {
            Neighborhood = "Manual SP",
            NormalizedName = "manual sp",
            City = storeCity,
            Latitude = null,
            Longitude = null,
            IsActive = true,
            Source = null
        };
        db.DeliveryNeighborhoods.Add(nbManualStoreCity);

        var nbManualOtherCity = new DeliveryNeighborhood
        {
            Neighborhood = "Manual RJ",
            NormalizedName = "manual rj",
            City = "Rio de Janeiro",
            Latitude = null,
            Longitude = null,
            IsActive = true,
            Source = null
        };
        db.DeliveryNeighborhoods.Add(nbManualOtherCity);

        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        var result = await sut.GetActiveDeliveryNeighborhoodsByStoreAsync(store.Id);

        result.Should().HaveCount(1);
        result.Single().Neighborhood.Should().Be("Manual SP");
    }

    [Fact]
    public async Task GetActiveDeliveryNeighborhoodsByStoreAsync_ShouldUsePreviewRadius_WhenSuppliedWithoutChangingPersistedRadius()
    {
        using var db = CreateDbContext();
        var storeCity = "Sao Paulo";

        var store = new Store
        {
            Name = "Loja SP Preview",
            Slug = "loja-sp-preview",
            MaxDeliveryRadiusKm = 10
        };
        db.Stores.Add(store);

        var storeAddress = new StoreAddress
        {
            StoreId = store.Id,
            City = storeCity,
            State = "SP",
            Latitude = -23.5505,
            Longitude = -46.6333
        };
        db.StoreAddresses.Add(storeAddress);

        var nbNear = new DeliveryNeighborhood
        {
            Neighborhood = "Bela Vista",
            NormalizedName = "bela vista",
            City = storeCity,
            CityId = Guid.NewGuid(),
            Latitude = -23.5580,
            Longitude = -46.6420,
            IsActive = true,
            Source = "test"
        };
        db.DeliveryNeighborhoods.Add(nbNear);

        var nbWithinPersistedButOutsidePreview = new DeliveryNeighborhood
        {
            Neighborhood = "Vila Mariana",
            NormalizedName = "vila mariana",
            City = storeCity,
            CityId = Guid.NewGuid(),
            Latitude = -23.5505,
            Longitude = -46.5840,
            IsActive = true,
            Source = "test"
        };
        db.DeliveryNeighborhoods.Add(nbWithinPersistedButOutsidePreview);

        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        var result = await sut.GetActiveDeliveryNeighborhoodsByStoreAsync(store.Id, radiusKm: 3);

        result.Should().Contain(x => x.Neighborhood == "Bela Vista");
        result.Should().NotContain(x => x.Neighborhood == "Vila Mariana");

        var persisted = await db.Stores.AsNoTracking().SingleAsync(x => x.Id == store.Id);
        persisted.MaxDeliveryRadiusKm.Should().Be(10);
    }

    [Fact]
    public async Task GetActiveDeliveryNeighborhoodsByStoreAsync_ShouldExpandPreviewRadius_WhenSuppliedIsLarger()
    {
        using var db = CreateDbContext();
        var storeCity = "Sao Paulo";

        var store = new Store
        {
            Name = "Loja SP Expand",
            Slug = "loja-sp-expand",
            MaxDeliveryRadiusKm = 3
        };
        db.Stores.Add(store);

        var storeAddress = new StoreAddress
        {
            StoreId = store.Id,
            City = storeCity,
            State = "SP",
            Latitude = -23.5505,
            Longitude = -46.6333
        };
        db.StoreAddresses.Add(storeAddress);

        var nbWithinPreview = new DeliveryNeighborhood
        {
            Neighborhood = "Vila Mariana",
            NormalizedName = "vila mariana",
            City = storeCity,
            CityId = Guid.NewGuid(),
            Latitude = -23.5505,
            Longitude = -46.5840,
            IsActive = true,
            Source = "test"
        };
        db.DeliveryNeighborhoods.Add(nbWithinPreview);

        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        var result = await sut.GetActiveDeliveryNeighborhoodsByStoreAsync(store.Id, radiusKm: 10);

        result.Should().Contain(x => x.Neighborhood == "Vila Mariana");
    }

    [Fact]
    public async Task UpdateDeliveryConfigAsync_ShouldKeepStoreOnlyManualArea_WhileRemovingKnownOutOfRadiusArea()
    {
        using var db = CreateDbContext();
        var storeCity = "Sao Paulo";

        var store = new Store
        {
            OwnerUserId = Guid.NewGuid(),
            Name = "Loja Manual",
            Slug = "loja-manual",
            MaxDeliveryRadiusKm = 10
        };
        db.Stores.Add(store);

        db.StoreAddresses.Add(new StoreAddress
        {
            StoreId = store.Id,
            City = storeCity,
            State = "SP",
            Latitude = -23.5505,
            Longitude = -46.6333
        });

        db.DeliveryNeighborhoods.AddRange(
            new DeliveryNeighborhood
            {
                Neighborhood = "Bela Vista",
                NormalizedName = "bela vista",
                City = storeCity,
                Latitude = -23.5580,
                Longitude = -46.6420,
                IsActive = true,
                Source = "test"
            },
            new DeliveryNeighborhood
            {
                Neighborhood = "Vila Mariana",
                NormalizedName = "vila mariana",
                City = storeCity,
                Latitude = -23.5505,
                Longitude = -46.5840,
                IsActive = true,
                Source = "test"
            });

        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        await sut.UpdateDeliveryConfigAsync(
            store.OwnerUserId,
            store.Id,
            deliveryFee: 5m,
            minimumOrderValue: 20m,
            freeShippingThreshold: null,
            freeShippingToday: false,
            deliveryAreas: new[]
            {
                new StoreDeliveryAreaDto { Neighborhood = "Bela Vista", DeliveryFee = 5m, IsActive = true },
                new StoreDeliveryAreaDto { Neighborhood = "Vila Mariana", DeliveryFee = 6m, IsActive = true },
                new StoreDeliveryAreaDto { Neighborhood = "Bairro Manual", DeliveryFee = 4m, IsActive = true }
            },
            maxDeliveryRadiusKm: 3,
            ipAddress: "127.0.0.1");

        await db.SaveChangesAsync();

        var storeAreas = await db.Set<StoreDeliveryArea>()
            .Where(x => x.StoreId == store.Id)
            .Select(x => x.Neighborhood)
            .ToListAsync();
        storeAreas.Should().BeEquivalentTo(new[] { "Bela Vista", "Bairro Manual" });

        var globalNeighborhoods = await db.DeliveryNeighborhoods
            .Where(x => x.City == storeCity)
            .Select(x => x.Neighborhood)
            .ToListAsync();
        globalNeighborhoods.Should().BeEquivalentTo(new[] { "Bela Vista", "Vila Mariana" });
    }

    [Fact]
    public async Task UpdateDeliveryConfigAsync_ShouldPersistMaxDeliveryRadiusKm_WhenSupplied()
    {
        using var db = CreateDbContext();
        var store = new Store
        {
            OwnerUserId = Guid.NewGuid(),
            Name = "Loja Raio",
            Slug = "loja-raio",
            MaxDeliveryRadiusKm = 5
        };
        db.Stores.Add(store);
        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        await sut.UpdateDeliveryConfigAsync(
            store.OwnerUserId,
            store.Id,
            deliveryFee: 5m,
            minimumOrderValue: 20m,
            freeShippingThreshold: null,
            freeShippingToday: false,
            deliveryAreas: null,
            maxDeliveryRadiusKm: 7.5,
            ipAddress: "127.0.0.1");

        var saved = await db.Stores.SingleAsync(x => x.Id == store.Id);
        saved.MaxDeliveryRadiusKm.Should().Be(7.5);
    }

    [Fact]
    public async Task UpdateDeliveryConfigAsync_ShouldPreserveMaxDeliveryRadiusKm_WhenOmitted()
    {
        using var db = CreateDbContext();
        var store = new Store
        {
            OwnerUserId = Guid.NewGuid(),
            Name = "Loja Raio Preservado",
            Slug = "loja-raio-preservado",
            MaxDeliveryRadiusKm = 5
        };
        db.Stores.Add(store);
        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        await sut.UpdateDeliveryConfigAsync(
            store.OwnerUserId,
            store.Id,
            deliveryFee: 5m,
            minimumOrderValue: 20m,
            freeShippingThreshold: null,
            freeShippingToday: false,
            deliveryAreas: null,
            ipAddress: "127.0.0.1");

        var saved = await db.Stores.SingleAsync(x => x.Id == store.Id);
        saved.MaxDeliveryRadiusKm.Should().Be(5);
    }

    [Fact]
    public async Task UpdateDeliveryConfigAsync_ShouldSetSaoPauloDate_WhenFreeShippingTodayEnabled()
    {
        using var db = CreateDbContext();
        var store = new Store
        {
            OwnerUserId = Guid.NewGuid(),
            Name = "Loja Frete Hoje",
            Slug = "loja-frete-hoje"
        };
        db.Stores.Add(store);
        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        await sut.UpdateDeliveryConfigAsync(
            store.OwnerUserId,
            store.Id,
            deliveryFee: 5m,
            minimumOrderValue: 20m,
            freeShippingThreshold: null,
            freeShippingToday: true,
            deliveryAreas: null,
            ipAddress: "127.0.0.1");

        var saved = await db.Stores.SingleAsync(x => x.Id == store.Id);
        saved.FreeShippingToday.Should().BeTrue();
        saved.FreeShippingTodayDate.Should().Be(StoreOpeningHoursCalculator.GetSaoPauloDate(DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task UpdateDeliveryConfigAsync_ShouldClearDate_WhenFreeShippingTodayDisabled()
    {
        using var db = CreateDbContext();
        var store = new Store
        {
            OwnerUserId = Guid.NewGuid(),
            Name = "Loja Frete Desligado",
            Slug = "loja-frete-desligado",
            FreeShippingToday = true,
            FreeShippingTodayDate = StoreOpeningHoursCalculator.GetSaoPauloDate(DateTimeOffset.UtcNow)
        };
        db.Stores.Add(store);
        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        await sut.UpdateDeliveryConfigAsync(
            store.OwnerUserId,
            store.Id,
            deliveryFee: 5m,
            minimumOrderValue: 20m,
            freeShippingThreshold: null,
            freeShippingToday: false,
            deliveryAreas: null,
            ipAddress: "127.0.0.1");

        var saved = await db.Stores.SingleAsync(x => x.Id == store.Id);
        saved.FreeShippingToday.Should().BeFalse();
        saved.FreeShippingTodayDate.Should().BeNull();
    }
}
