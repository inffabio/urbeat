using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Services;

namespace Urbeat.UnitTests.Infrastructure.Services;

public sealed class AddressLookupServiceTests
{
    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-address-tests-{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static AddressLookupService CreateSut(ApplicationDbContext db, IViaCepService viaCep)
    {
        return new AddressLookupService(
            db,
            viaCep,
            new Mock<ILogger<AddressLookupService>>().Object,
            new NullGeographicCache());
    }

    private static (City City, DeliveryNeighborhood Neighborhood, DeliveryStreet Street) Seed(
        ApplicationDbContext db,
        string zipCode)
    {
        var city = new City
        {
            Name = "São Paulo",
            Uf = "SP",
            IbgeCode = "3550308",
            NormalizedName = "sao paulo"
        };
        var neighborhood = new DeliveryNeighborhood
        {
            CityId = city.Id,
            City = city.Name,
            Neighborhood = "Bela Vista",
            NormalizedName = "bela vista",
            Kind = "official_cnefe",
            CoordinateStatus = "computed",
            Latitude = -23.5614,
            Longitude = -46.6559
        };
        var street = new DeliveryStreet
        {
            CityId = city.Id,
            CityEntity = city,
            NeighborhoodId = neighborhood.Id,
            NeighborhoodEntity = neighborhood,
            Name = "Avenida Paulista",
            NormalizedName = "avenida paulista",
            Latitude = -23.5614,
            Longitude = -46.6559
        };

        db.Cities.Add(city);
        db.DeliveryNeighborhoods.Add(neighborhood);
        db.DeliveryStreets.Add(street);
        db.DeliveryStreetZipCodes.Add(new DeliveryStreetZipCode
        {
            StreetId = street.Id,
            Street = street,
            ZipCode = zipCode,
            IsPrimary = true,
            Source = "cnefe",
            SourceVersion = "CNEFE_2022"
        });

        return (city, neighborhood, street);
    }

    [Fact]
    public async Task Local_cep_is_preferred_and_returns_the_aggregated_coordinate()
    {
        using var db = CreateDbContext();
        Seed(db, "01310100");
        var run = new ImportRun
        {
            SourceName = "CNEFE",
            SourceVersion = "CNEFE_2022",
            SourceChecksum = new string('b', 64),
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

        var viaCep = new Mock<IViaCepService>(MockBehavior.Strict);

        var result = await CreateSut(db, viaCep.Object).LookupByCepAsync("01310-100");

        result.Should().NotBeNull();
        result!.Cep.Should().Be("01310100");
        result.Street.Should().Be("Avenida Paulista");
        result.Neighborhood.Should().Be("Bela Vista");
        result.City.Should().Be("São Paulo");
        result.State.Should().Be("SP");
        result.IbgeCode.Should().Be("3550308");
        result.Latitude.Should().Be(-23.5614);
        result.Longitude.Should().Be(-46.6559);
        result.Source.Should().Be("local");
        result.DatasetVersion.Should().Be("CNEFE_2022");
        result.MultipleMatches.Should().BeFalse();
        viaCep.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Multiple_logradouros_are_flagged()
    {
        using var db = CreateDbContext();
        var (city, neighborhood, firstStreet) = Seed(db, "01310100");
        var secondStreet = new DeliveryStreet
        {
            CityId = city.Id,
            NeighborhoodId = neighborhood.Id,
            Name = "Rua Augusta",
            NormalizedName = "rua augusta",
            Latitude = -23.5550,
            Longitude = -46.6570
        };
        db.DeliveryStreets.Add(secondStreet);
        db.DeliveryStreetZipCodes.Add(new DeliveryStreetZipCode
        {
            StreetId = secondStreet.Id,
            ZipCode = "01310100",
            IsPrimary = false,
            Source = "cnefe",
            SourceVersion = "CNEFE_2022"
        });
        await db.SaveChangesAsync();

        var result = await CreateSut(db, new Mock<IViaCepService>().Object).LookupByCepAsync("01310100");

        result.Should().NotBeNull();
        result!.MultipleMatches.Should().BeTrue();
        result.Matches.Should().HaveCount(2);
        result.DatasetVersion.Should().BeNull();
        firstStreet.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task Unknown_cep_falls_back_to_viacep_without_persisting_it()
    {
        using var db = CreateDbContext();
        var viaCep = new Mock<IViaCepService>();
        viaCep
            .Setup(x => x.LookupAsync("99999999", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ViaCepAddressResponseDto
            {
                Cep = "99999999",
                Street = "Rua Externa",
                Neighborhood = "Centro",
                City = "Cidade Externa",
                State = "RS"
            });

        var result = await CreateSut(db, viaCep.Object).LookupByCepAsync("99999999");

        result.Should().NotBeNull();
        result!.Source.Should().Be("viacep");
        result.DatasetVersion.Should().BeNull();
        (await db.DeliveryStreets.CountAsync()).Should().Be(0);
        (await db.DeliveryStreetZipCodes.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Invalid_cep_returns_null_without_calling_viacep()
    {
        using var db = CreateDbContext();
        var viaCep = new Mock<IViaCepService>(MockBehavior.Strict);

        var result = await CreateSut(db, viaCep.Object).LookupByCepAsync("123");

        result.Should().BeNull();
        viaCep.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Street_search_requires_a_city_or_uf_scope()
    {
        using var db = CreateDbContext();
        Seed(db, "01310100");
        await db.SaveChangesAsync();

        var sut = CreateSut(db, new Mock<IViaCepService>().Object);

        (await sut.SearchStreetsAsync(null, null, "paulista")).Should().BeEmpty();

        var byUf = await sut.SearchStreetsAsync(null, "SP", "avenida");
        byUf.Should().ContainSingle().Which.Name.Should().Be("Avenida Paulista");
    }
}
