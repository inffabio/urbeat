using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Urbeat.Infrastructure.Persistence;

namespace Urbeat.UnitTests.Infrastructure.Persistence;

public sealed class DeliveryNeighborhoodQueryExtensionsTests
{
    [Fact]
    public void WhereNotAliasSource_ShouldTranslateToACorrelatedNotExists_OnPostgreSql()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            // No connection is opened: ToQueryString only compiles the LINQ tree with the relational
            // provider, which is what proves the alias rule stays server-side.
            .UseNpgsql("Host=localhost;Database=urbeat;Username=postgres;Password=postgres")
            .Options;
        using var db = new ApplicationDbContext(options);

        var sql = db.DeliveryNeighborhoods
            .AsNoTracking()
            .Where(x => x.IsActive)
            .WhereNotAliasSource(db)
            .ToQueryString();

        sql.Should().Contain("NOT EXISTS");
        sql.Should().Contain("\"DeliveryNeighborhoodAliases\"");
        sql.Should().Contain("\"IsActive\"");
        sql.Should().Contain("\"IsApproved\"");
        sql.Should().Contain("\"NormalizedAlias\"");
        sql.Should().Contain("\"CanonicalNeighborhoodId\" <>");
    }

    [Fact]
    public void RadiusCandidateComposition_ShouldStayTranslatable_WithTheAliasExclusion()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=urbeat;Username=postgres;Password=postgres")
            .Options;
        using var db = new ApplicationDbContext(options);

        var cityId = Guid.NewGuid();
        var normalizedCity = "maricá";

        // Mirrors the store radius path: city scope + alias exclusion + bounding box.
        var sql = db.DeliveryNeighborhoods
            .AsNoTracking()
            .Where(x => x.IsActive && (x.CityId == cityId || x.City.ToLower() == normalizedCity))
            .WhereNotAliasSource(db)
            .Where(x => x.Latitude != null
                && x.Longitude != null
                && x.Latitude >= -22.99
                && x.Latitude <= -22.85
                && x.Longitude >= -42.90
                && x.Longitude <= -42.74)
            .ToQueryString();

        sql.Should().Contain("NOT EXISTS");
        sql.Should().Contain("lower(");
        sql.Should().Contain("\"Latitude\" >=");
        sql.Should().Contain("\"DeliveryNeighborhoodAliases\"");
    }
}
