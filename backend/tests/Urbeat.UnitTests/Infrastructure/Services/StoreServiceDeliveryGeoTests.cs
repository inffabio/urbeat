using AutoMapper;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Urbeat.UnitTests.Infrastructure.Services;

/// <summary>
/// Covers the geographic base migration behaviours: identifier resolution, complete-circle radius
/// on every cardinal direction (including the exact boundary), manual neighborhoods, dataset
/// version propagation and dual write of <see cref="StoreDeliveryArea.NeighborhoodId"/>.
/// </summary>
public sealed class StoreServiceDeliveryGeoTests
{
    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-geo-tests-{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static StoreService CreateSut(ApplicationDbContext db)
    {
        return new StoreService(
            db,
            new Mock<IMapper>().Object,
            new Mock<IStoreReadRepository>().Object,
            new Urbeat.Infrastructure.Persistence.UnitOfWork.EfUnitOfWork(db),
            new Mock<IImageUploadService>().Object);
    }

    private static (Store Store, City City) SeedStore(
        ApplicationDbContext db,
        double latitude,
        double longitude,
        double radiusKm)
    {
        var city = new City
        {
            Name = "São Paulo",
            Uf = "SP",
            IbgeCode = "3550308",
            NormalizedName = "sao paulo",
            Source = "ibge_localidades"
        };
        db.Cities.Add(city);

        var store = new Store
        {
            Name = "Loja Geo",
            Slug = $"loja-geo-{Guid.NewGuid():N}",
            MaxDeliveryRadiusKm = radiusKm
        };
        db.Stores.Add(store);

        db.StoreAddresses.Add(new StoreAddress
        {
            StoreId = store.Id,
            City = "São Paulo",
            State = "SP",
            Latitude = latitude,
            Longitude = longitude
        });

        return (store, city);
    }

    private static DeliveryNeighborhood AddNeighborhood(
        ApplicationDbContext db,
        City city,
        string name,
        double? latitude,
        double? longitude,
        string kind = "official_cnefe",
        string status = "computed")
    {
        var neighborhood = new DeliveryNeighborhood
        {
            CityId = city.Id,
            City = city.Name,
            Neighborhood = name,
            NormalizedName = Urbeat.Domain.Geo.NeighborhoodName.Normalize(name),
            Kind = kind,
            CoordinateStatus = status,
            Latitude = latitude,
            Longitude = longitude,
            Source = "cnefe"
        };
        db.DeliveryNeighborhoods.Add(neighborhood);
        return neighborhood;
    }

    [Theory]
    // 0.1 degree is ~11.1 km at this latitude. All four cardinal directions must behave the same.
    [InlineData(-23.6505, -46.6333)] // south
    [InlineData(-23.4505, -46.6333)] // north
    [InlineData(-23.5505, -46.7333)] // west
    [InlineData(-23.5505, -46.5333)] // east
    public async Task Radius_is_a_complete_circle_without_directional_bias(double latitude, double longitude)
    {
        using var db = CreateDbContext();
        var (store, city) = SeedStore(db, -23.5505, -46.6333, 12);
        var neighborhood = AddNeighborhood(db, city, "Alvo", latitude, longitude);
        await db.SaveChangesAsync();

        var result = await CreateSut(db).GetActiveDeliveryNeighborhoodsByStoreAsync(store.Id);

        result.Should().ContainSingle(x => x.Id == neighborhood.Id);
        var item = result.Single(x => x.Id == neighborhood.Id);
        item.Eligible.Should().BeTrue();
        item.DistanceKm.Should().BeGreaterThan(9.5).And.BeLessThan(12);
        item.DatasetVersion.Should().BeNull();
    }

    [Fact]
    public async Task Exact_boundary_is_eligible_and_the_next_metre_is_not()
    {
        using var db = CreateDbContext();
        const double oneDegreeKm = 6371 * Math.PI / 180;
        var (store, city) = SeedStore(db, 0, -46.6, oneDegreeKm);

        var boundary = AddNeighborhood(db, city, "No limite", 1, -46.6);
        AddNeighborhood(db, city, "Fora", 1.0000001, -46.6);
        await db.SaveChangesAsync();

        var result = await CreateSut(db).GetActiveDeliveryNeighborhoodsByStoreAsync(store.Id);

        result.Should().ContainSingle();
        result.Single().Id.Should().Be(boundary.Id);
        result.Single().Eligible.Should().BeTrue();
    }

    [Fact]
    public async Task Manual_neighborhood_is_returned_separately_without_distance()
    {
        using var db = CreateDbContext();
        var (store, city) = SeedStore(db, -23.5505, -46.6333, 5);
        var manual = AddNeighborhood(db, city, "Condomínio Novo", null, null, kind: "manual", status: "manual");
        await db.SaveChangesAsync();

        var result = await CreateSut(db).GetActiveDeliveryNeighborhoodsByStoreAsync(store.Id);

        var item = result.Should().ContainSingle().Subject;
        item.Id.Should().Be(manual.Id);
        item.DistanceKm.Should().BeNull();
        item.Eligible.Should().BeFalse();
        item.RequiresManualReview.Should().BeTrue();
    }

    [Fact]
    public async Task Valid_radius_without_matches_returns_an_empty_result_instead_of_the_whole_city()
    {
        using var db = CreateDbContext();
        var (store, city) = SeedStore(db, -23.5505, -46.6333, 1);
        AddNeighborhood(db, city, "Longe", -23.9, -46.9);
        await db.SaveChangesAsync();

        var result = await CreateSut(db).GetActiveDeliveryNeighborhoodsByStoreAsync(store.Id);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Homonyms_in_another_municipality_are_not_returned()
    {
        using var db = CreateDbContext();
        var (store, city) = SeedStore(db, -23.5505, -46.6333, 10);
        var target = AddNeighborhood(db, city, "Centro", -23.55, -46.64);

        var otherCity = new City
        {
            Name = "Campinas",
            Uf = "SP",
            IbgeCode = "3509502",
            NormalizedName = "campinas"
        };
        db.Cities.Add(otherCity);
        var homonym = AddNeighborhood(db, otherCity, "Centro", -23.55, -46.64);
        await db.SaveChangesAsync();

        var result = await CreateSut(db).GetActiveDeliveryNeighborhoodsByStoreAsync(store.Id);

        result.Select(x => x.Id).Should().ContainSingle().Which.Should().Be(target.Id);
        result.Should().NotContain(x => x.Id == homonym.Id);
    }

    [Fact]
    public async Task Dataset_version_comes_from_the_active_import_dataset()
    {
        using var db = CreateDbContext();
        var (store, city) = SeedStore(db, -23.5505, -46.6333, 10);
        AddNeighborhood(db, city, "Centro", -23.55, -46.64);

        var run = new ImportRun
        {
            SourceName = "CNEFE",
            SourceVersion = "CNEFE_2022",
            SourceChecksum = new string('a', 64),
            Uf = "SP",
            Status = "published"
        };
        db.ImportRuns.Add(run);
        db.ImportDatasets.Add(new ImportDataset
        {
            SourceVersion = "CNEFE_2022",
            Uf = "SP",
            RunId = run.Id,
            IsActive = true,
            ActivatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await CreateSut(db).GetActiveDeliveryNeighborhoodsByStoreAsync(store.Id);

        result.Should().ContainSingle();
        result.Single().DatasetVersion.Should().Be("CNEFE_2022");
    }

    [Fact]
    public async Task Delivery_config_dual_writes_the_neighborhood_identifier_and_haversine_distance()
    {
        using var db = CreateDbContext();
        var (store, city) = SeedStore(db, -23.5505, -46.6333, 10);
        var center = AddNeighborhood(db, city, "Centro", -23.55, -46.64);
        store.OwnerUserId = Guid.Empty;
        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        await sut.UpdateDeliveryConfigAsync(
            Guid.Empty,
            store.Id,
            deliveryFee: 5m,
            minimumOrderValue: 0m,
            freeShippingThreshold: null,
            freeShippingToday: false,
            deliveryAreas:
            [
                new StoreDeliveryAreaDto { Neighborhood = "Centro", DeliveryFee = 5m, IsActive = true },
                new StoreDeliveryAreaDto { Neighborhood = "centro", DeliveryFee = 7m, IsActive = true }
            ],
            maxDeliveryRadiusKm: null,
            ipAddress: null);

        var area = await db.StoreDeliveryAreas.SingleAsync();
        area.Neighborhood.Should().Be("Centro");
        area.NeighborhoodId.Should().Be(center.Id);
        area.DistanceKm.Should().NotBeNull();
        area.DistanceMethod.Should().Be("haversine");
        area.DistanceKm!.Value.Should().BeLessThan(2);

        // Two spellings that normalize to the same neighborhood are deduplicated.
        (await db.StoreDeliveryAreas.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Delivery_config_drops_areas_outside_the_radius()
    {
        using var db = CreateDbContext();
        var (store, city) = SeedStore(db, -23.5505, -46.6333, 2);
        var near = AddNeighborhood(db, city, "Perto", -23.56, -46.63);
        var far = AddNeighborhood(db, city, "Longe", -23.9, -46.9);
        store.OwnerUserId = Guid.Empty;
        await db.SaveChangesAsync();

        var sut = CreateSut(db);
        await sut.UpdateDeliveryConfigAsync(
            Guid.Empty,
            store.Id,
            deliveryFee: 5m,
            minimumOrderValue: 0m,
            freeShippingThreshold: null,
            freeShippingToday: false,
            deliveryAreas:
            [
                new StoreDeliveryAreaDto { Neighborhood = "Perto", DeliveryFee = 5m, IsActive = true },
                new StoreDeliveryAreaDto { Neighborhood = "Longe", DeliveryFee = 9m, IsActive = true }
            ],
            maxDeliveryRadiusKm: 2,
            ipAddress: null);

        var areas = await db.StoreDeliveryAreas.ToListAsync();
        areas.Should().ContainSingle();
        areas.Single().NeighborhoodId.Should().Be(near.Id);
        areas.Should().NotContain(x => x.NeighborhoodId == far.Id);
    }
}
