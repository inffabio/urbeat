using AutoMapper;
using FluentAssertions;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
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
    public async Task GetActiveDeliveryNeighborhoodsByStoreAsync_ShouldExcludeNeighborhoodsWithoutCoordinates_WhenRadiusIsSet()
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
            Source = "test"
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

        result.Should().HaveCount(1);
        result.Single().Neighborhood.Should().Be("Com Coordenadas");
    }
}
