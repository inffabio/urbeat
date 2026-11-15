using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Urbeat.Infrastructure.Persistence;
using Xunit;

namespace Urbeat.UnitTests.Domain;

public sealed class DeliveryNeighborhoodAliasTests
{
    [Fact]
    public void Model_ShouldKeepAliasesUniqueByCityAndNormalizedName_WhenActive()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-alias-model-{Guid.NewGuid()}")
            .Options);

        var index = db.Model.FindEntityType("Urbeat.Domain.Entities.DeliveryNeighborhoodAlias")!
            .GetIndexes()
            .Single(x => x.Properties.Select(p => p.Name)
                .SequenceEqual(new[] { "CityId", "NormalizedAlias" }));

        index.IsUnique.Should().BeTrue();
        index.GetFilter().Should().Contain("\"IsActive\" = TRUE");
    }
}
