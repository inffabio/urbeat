using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.PostgreSql;
using Urbeat.Domain.Entities;
using Urbeat.Domain.Geo;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Persistence.Migrations;
using Xunit.Abstractions;

namespace Urbeat.IntegrationTests.Relational;

/// <summary>
/// Proves the additive geographic migration against a real PostgreSQL: the legacy columns stay
/// untouched, canonical names are backfilled with a collision guard, provenance defaults are
/// explicit, and <see cref="StoreDeliveryArea.NeighborhoodId"/> is resolved by store city +
/// normalized name with unresolved rows quarantined instead of deleted.
/// </summary>
public sealed class CnefeGeographicBaseMigrationRelationalTests
{
    private readonly ITestOutputHelper _output;

    public CnefeGeographicBaseMigrationRelationalTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Migration_backfills_canonical_names_and_delivery_area_identifiers_without_data_loss()
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

            Guid resolvedAreaId;
            Guid unresolvedAreaId;

            await using (var setupContext = new ApplicationDbContext(options))
            {
                await setupContext.Database.EnsureCreatedAsync();

                // Return the schema to its pre-migration shape so Up() really performs the change.
                await setupContext.Database.ExecuteSqlRawAsync("""
                    DROP TABLE IF EXISTS "DeliveryStreetZipCodes", "DeliveryStreets", "ImportDatasets",
                        "ImportErrors", "ImportRuns", "StoreDeliveryAreaMigrationIssues" CASCADE;
                    ALTER TABLE "StoreDeliveryAreas" DROP COLUMN IF EXISTS "NeighborhoodId" CASCADE;
                    ALTER TABLE "StoreDeliveryAreas" DROP COLUMN IF EXISTS "DistanceKm" CASCADE;
                    ALTER TABLE "StoreDeliveryAreas" DROP COLUMN IF EXISTS "DistanceMethod" CASCADE;
                    ALTER TABLE "DeliveryNeighborhoods" DROP COLUMN IF EXISTS "Kind" CASCADE;
                    ALTER TABLE "DeliveryNeighborhoods" DROP COLUMN IF EXISTS "CoordinateMethod" CASCADE;
                    ALTER TABLE "DeliveryNeighborhoods" DROP COLUMN IF EXISTS "CoordinatePointCount" CASCADE;
                    ALTER TABLE "DeliveryNeighborhoods" DROP COLUMN IF EXISTS "CoordinateSourceVersion" CASCADE;
                    ALTER TABLE "DeliveryNeighborhoods" DROP COLUMN IF EXISTS "CoordinateCalculatedAtUtc" CASCADE;
                    ALTER TABLE "DeliveryNeighborhoods" DROP COLUMN IF EXISTS "CoordinateStatus" CASCADE;
                    ALTER TABLE "DeliveryNeighborhoods" DROP COLUMN IF EXISTS "RepresentativeLatitude" CASCADE;
                    ALTER TABLE "DeliveryNeighborhoods" DROP COLUMN IF EXISTS "RepresentativeLongitude" CASCADE;
                    ALTER TABLE "DeliveryNeighborhoods" DROP COLUMN IF EXISTS "RepresentativeMethod" CASCADE;
                    ALTER TABLE "DeliveryNeighborhoods" DROP COLUMN IF EXISTS "LocalityType" CASCADE;
                    ALTER TABLE "Cities" DROP COLUMN IF EXISTS "NormalizedName" CASCADE;
                    ALTER TABLE "Cities" DROP COLUMN IF EXISTS "CoordinateMethod" CASCADE;
                    ALTER TABLE "Cities" DROP COLUMN IF EXISTS "Source" CASCADE;
                    ALTER TABLE "Cities" DROP COLUMN IF EXISTS "SourceVersion" CASCADE;
                    DROP INDEX IF EXISTS "IX_DeliveryNeighborhoods_CityId_IsActive";
                    DROP INDEX IF EXISTS "IX_DeliveryNeighborhoods_CityId_Kind";
                    DROP INDEX IF EXISTS "IX_Cities_Uf_NormalizedName";
                    """);

                var city = new City
                {
                    Name = "São Paulo",
                    Uf = "SP",
                    IbgeCode = "3550308",
                    Latitude = -23.5505,
                    Longitude = -46.6333
                };
                var otherCity = new City
                {
                    Name = "Campinas",
                    Uf = "SP",
                    IbgeCode = "3509502"
                };
                setupContext.Cities.AddRange(city, otherCity);

                var official = new DeliveryNeighborhood
                {
                    CityId = city.Id,
                    City = "São Paulo",
                    Neighborhood = "Santa-Rita",
                    NormalizedName = "santa-rita",
                    Latitude = -23.56,
                    Longitude = -46.64,
                    Source = "openstreetmap"
                };
                var manual = new DeliveryNeighborhood
                {
                    CityId = city.Id,
                    City = "São Paulo",
                    Neighborhood = "Condomínio Fechado",
                    NormalizedName = "condominio fechado",
                    Source = null
                };
                setupContext.DeliveryNeighborhoods.AddRange(official, manual);

                var store = new Store
                {
                    Name = "Loja Migração",
                    Slug = "loja-migracao-geo",
                    PhoneNumber = "11999999999"
                };
                setupContext.Stores.Add(store);
                setupContext.StoreAddresses.Add(new StoreAddress
                {
                    StoreId = store.Id,
                    City = "Sao Paulo",
                    State = "SP",
                    Latitude = -23.5505,
                    Longitude = -46.6333
                });

                var resolvedArea = new StoreDeliveryArea
                {
                    StoreId = store.Id,
                    Neighborhood = "Santa Rita",
                    DeliveryFee = 7m
                };
                var unresolvedArea = new StoreDeliveryArea
                {
                    StoreId = store.Id,
                    Neighborhood = "Bairro Inexistente",
                    DeliveryFee = 9m
                };
                setupContext.StoreDeliveryAreas.AddRange(resolvedArea, unresolvedArea);

                await setupContext.SaveChangesAsync();

                resolvedAreaId = resolvedArea.Id;
                unresolvedAreaId = unresolvedArea.Id;
                official.Id.Should().NotBe(Guid.Empty);
            }

            await using (var migrationContext = new ApplicationDbContext(options))
            {
                var generator = migrationContext.GetService<IMigrationsSqlGenerator>();
                var operations = new AddCnefeGeographicBase().UpOperations;
                foreach (var command in generator.Generate(operations, migrationContext.Model))
                {
                    await migrationContext.Database.ExecuteSqlRawAsync(command.CommandText);
                }
            }

            await using (var verifyContext = new ApplicationDbContext(options))
            {
                var city = await verifyContext.Cities.SingleAsync(x => x.IbgeCode == "3550308");
                city.NormalizedName.Should().Be("sao paulo");
                city.Name.Should().Be("São Paulo");
                city.Source.Should().Be("openstreetmap");

                var official = await verifyContext.DeliveryNeighborhoods
                    .SingleAsync(x => x.Neighborhood == "Santa-Rita");
                official.NormalizedName.Should().Be("santa rita");
                official.Kind.Should().Be(GeographicProvenance.NeighborhoodKind.Legacy);
                official.CoordinateStatus.Should().Be(GeographicProvenance.CoordinateStatus.Computed);
                official.Latitude.Should().Be(-23.56);
                official.CoordinateMethod.Should().BeNull();

                var manual = await verifyContext.DeliveryNeighborhoods
                    .SingleAsync(x => x.Neighborhood == "Condomínio Fechado");
                manual.Kind.Should().Be(GeographicProvenance.NeighborhoodKind.Manual);
                manual.CoordinateStatus.Should().Be(GeographicProvenance.CoordinateStatus.Manual);

                var resolvedArea = await verifyContext.StoreDeliveryAreas.SingleAsync(x => x.Id == resolvedAreaId);
                resolvedArea.Neighborhood.Should().Be("Santa Rita");
                resolvedArea.NeighborhoodId.Should().Be(official.Id);
                resolvedArea.DistanceKm.Should().NotBeNull();
                resolvedArea.DistanceKm!.Value.Should().BeLessThan(2);
                resolvedArea.DistanceMethod.Should().Be("haversine");
                resolvedArea.DeliveryFee.Should().Be(7m);

                var unresolvedArea = await verifyContext.StoreDeliveryAreas.SingleAsync(x => x.Id == unresolvedAreaId);
                unresolvedArea.Neighborhood.Should().Be("Bairro Inexistente");
                unresolvedArea.NeighborhoodId.Should().BeNull();
                unresolvedArea.DeliveryFee.Should().Be(9m);

                var issue = await verifyContext.StoreDeliveryAreaMigrationIssues.SingleAsync();
                issue.StoreDeliveryAreaId.Should().Be(unresolvedAreaId);
                issue.Reason.Should().Be("unresolved_neighborhood");
                issue.NormalizedName.Should().Be("bairro inexistente");
                issue.City.Should().Be("Sao Paulo");
                issue.Uf.Should().Be("SP");

                // The identifiers were added without removing the transitional textual column.
                var columns = await verifyContext.Database
                    .SqlQueryRaw<string>(
                        """SELECT column_name AS "Value" FROM information_schema.columns WHERE table_name = 'StoreDeliveryAreas'""")
                    .ToListAsync();
                columns.Should().Contain("Neighborhood").And.Contain("NeighborhoodId");

                // The pre-existing plain StoreId index is preserved next to the new filtered unique
                // index, and the migration does not create a duplicate with the same name.
                var indexNames = await verifyContext.Database
                    .SqlQueryRaw<string>(
                        """SELECT indexname AS "Value" FROM pg_indexes WHERE tablename = 'StoreDeliveryAreas'""")
                    .ToListAsync();
                indexNames.Should().Contain("IX_StoreDeliveryAreas_StoreId");
                indexNames.Should().Contain("IX_StoreDeliveryAreas_StoreId_NeighborhoodId");
                indexNames.Count(name => name == "IX_StoreDeliveryAreas_StoreId").Should().Be(1);
            }
        }
        finally
        {
            await container.DisposeAsync();
        }
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
