using AutoMapper;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Mappings;
using Urbeat.Domain.Entities;
using Urbeat.Domain.Geo;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Persistence.UnitOfWork;
using Urbeat.Infrastructure.Services;
using Urbeat.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace Urbeat.IntegrationTests.Relational;

/// <summary>
/// The store neighborhood endpoint loads bounding-box candidates straight from
/// <see cref="DeliveryNeighborhood"/> rows, so the alias exclusion must be translated to SQL by
/// PostgreSQL. Only a relational provider proves that: the InMemory provider would happily filter
/// after materialization. This test seeds a Maricá group with three spellings of the same
/// neighborhood plus two approved aliases and asserts that both the radius and the city-list paths
/// return only the canonical row, while a manual neighborhood without an alias keeps appearing.
/// It is skipped when Docker is unavailable.
/// </summary>
public sealed class StoreDeliveryNeighborhoodAliasRelationalTests
{
    private readonly ITestOutputHelper _output;

    public StoreDeliveryNeighborhoodAliasRelationalTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task GetActiveDeliveryNeighborhoodsByStoreAsync_ShouldHideAliasSources_OnRadiusAndCityListPaths()
    {
        var container = await StartPostgresOrSkipAsync();
        if (container is null)
        {
            return;
        }

        try
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(container.GetConnectionString())
                .Options;

            Guid storeId;
            Guid canonicalId;
            Guid manualId;
            Guid[] aliasSourceIds;

            await using (var setupContext = new ApplicationDbContext(options))
            {
                await setupContext.Database.EnsureCreatedAsync();

                var city = new City
                {
                    Name = "Maricá",
                    Uf = "RJ",
                    IbgeCode = "3302700",
                    NormalizedName = "marica"
                };
                setupContext.Cities.Add(city);

                var store = new Store
                {
                    Name = "Loja Maricá",
                    Slug = "loja-marica-alias",
                    PhoneNumber = "21999999999",
                    MaxDeliveryRadiusKm = 10
                };
                setupContext.Stores.Add(store);
                setupContext.StoreAddresses.Add(new StoreAddress
                {
                    StoreId = store.Id,
                    City = "Maricá",
                    State = "RJ",
                    Latitude = -22.9194,
                    Longitude = -42.8186
                });

                var canonical = new DeliveryNeighborhood
                {
                    CityId = city.Id,
                    City = "Maricá",
                    Neighborhood = "Chácara de Inoã",
                    DisplayName = "Chácara de Inoã",
                    NormalizedName = "chacara de inoa",
                    Latitude = -22.9250,
                    Longitude = -42.8230,
                    IsActive = true,
                    Source = "openstreetmap"
                };
                var pluralAliasSource = new DeliveryNeighborhood
                {
                    CityId = city.Id,
                    City = "Maricá",
                    Neighborhood = "CHACARAS DE INOA",
                    NormalizedName = "chacaras de inoa",
                    Latitude = -22.9251,
                    Longitude = -42.8231,
                    IsActive = true,
                    Source = "openstreetmap"
                };
                var shortAliasSource = new DeliveryNeighborhood
                {
                    CityId = city.Id,
                    City = "Maricá",
                    Neighborhood = "CHACARAS INOA",
                    NormalizedName = "chacaras inoa",
                    Latitude = -22.9252,
                    Longitude = -42.8232,
                    IsActive = true,
                    Source = "openstreetmap"
                };
                var manual = new DeliveryNeighborhood
                {
                    CityId = city.Id,
                    City = "Maricá",
                    Neighborhood = "Condomínio Village",
                    NormalizedName = "condominio village",
                    Kind = GeographicProvenance.NeighborhoodKind.Manual,
                    CoordinateStatus = GeographicProvenance.CoordinateStatus.Manual,
                    IsActive = true
                };
                setupContext.DeliveryNeighborhoods.AddRange(canonical, pluralAliasSource, shortAliasSource, manual);

                setupContext.DeliveryNeighborhoodAliases.AddRange(
                    new DeliveryNeighborhoodAlias
                    {
                        CityId = city.Id,
                        CanonicalNeighborhoodId = canonical.Id,
                        Alias = pluralAliasSource.Neighborhood,
                        NormalizedAlias = pluralAliasSource.NormalizedName,
                        IsActive = true,
                        IsApproved = true,
                        ApprovedAtUtc = DateTime.UtcNow
                    },
                    new DeliveryNeighborhoodAlias
                    {
                        CityId = city.Id,
                        CanonicalNeighborhoodId = canonical.Id,
                        Alias = shortAliasSource.Neighborhood,
                        NormalizedAlias = shortAliasSource.NormalizedName,
                        IsActive = true,
                        IsApproved = true,
                        ApprovedAtUtc = DateTime.UtcNow
                    });

                await setupContext.SaveChangesAsync();

                storeId = store.Id;
                canonicalId = canonical.Id;
                manualId = manual.Id;
                aliasSourceIds = [pluralAliasSource.Id, shortAliasSource.Id];
            }

            var mapper = new MapperConfiguration(configuration => configuration.AddProfile<EntityToDtoProfile>())
                .CreateMapper();

            await using (var radiusContext = new ApplicationDbContext(options))
            {
                var service = CreateService(radiusContext, mapper);

                var radiusResult = await service.GetActiveDeliveryNeighborhoodsByStoreAsync(storeId, radiusKm: 10);

                radiusResult.Select(x => x.Id).Should().BeEquivalentTo([canonicalId, manualId]);
                radiusResult.Should().NotContain(x => aliasSourceIds.Contains(x.Id));

                var canonical = radiusResult.Single(x => x.Id == canonicalId);
                canonical.CanonicalNeighborhoodId.Should().Be(canonicalId);
                canonical.DisplayName.Should().Be("CHÁCARA DE INOÃ");
                canonical.DistanceKm.Should().NotBeNull();
                canonical.Eligible.Should().BeTrue();

                var manual = radiusResult.Single(x => x.Id == manualId);
                manual.Eligible.Should().BeFalse();
                manual.RequiresManualReview.Should().BeTrue();
            }

            await using (var cityContext = new ApplicationDbContext(options))
            {
                var service = CreateService(cityContext, mapper);

                // A non-positive radius disables the geographic path, so this exercises the city list.
                var cityResult = await service.GetActiveDeliveryNeighborhoodsByStoreAsync(storeId, radiusKm: 0);

                cityResult.Select(x => x.Id).Should().BeEquivalentTo([canonicalId, manualId]);
                cityResult.Should().NotContain(x => aliasSourceIds.Contains(x.Id));
                cityResult.Should().OnlyContain(x => x.DistanceKm == null);
            }
        }
        finally
        {
            await container.DisposeAsync();
        }
    }

    private static StoreService CreateService(ApplicationDbContext context, IMapper mapper)
        => new(
            context,
            mapper,
            new UnusedStoreReadRepository(),
            new EfUnitOfWork(context),
            new FakeImageUploadService());

    private sealed class UnusedStoreReadRepository : IStoreReadRepository
    {
        public Task<StoreResponseDto?> GetByOwnerAsync(Guid ownerUserId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyCollection<StorePublicListItemDto>> ListPublicAsync(
            string? cuisineType,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<StorePublicDetailsDto?> GetPublicByIdAsync(Guid storeId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<StorePublicDetailsDto?> GetPublicBySlugAsync(string slug, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<StorePublicDetailsDto?> GetPublicByPathAsync(string storePath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private async Task<PostgreSqlContainer?> StartPostgresOrSkipAsync()
    {
        PostgreSqlContainer? container = null;

        try
        {
            container = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .WithDatabase("urbeat")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();

            await container.StartAsync();
            return container;
        }
        catch (Exception exception)
        {
            if (container is not null)
            {
                await container.DisposeAsync();
            }

            _output.WriteLine($"SKIPPED (Docker unavailable): {exception.Message}");
            return null;
        }
    }
}
