using FluentAssertions;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.UnitTests.Infrastructure.Persistence;

public sealed class ApplicationDbContextModelTests
{
    [Fact]
    public void Order_ShouldHaveStoreStatusCreatedAtIndexForSellerOperationalQueries()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-model-{Guid.NewGuid()}")
            .Options;
        using var db = new ApplicationDbContext(options);

        var orderType = db.Model.FindEntityType(typeof(Order));
        var index = orderType?.GetIndexes().SingleOrDefault(x =>
            x.Properties.Select(property => property.Name).SequenceEqual(new[]
            {
                nameof(Order.StoreId),
                nameof(Order.Status),
                nameof(Order.CreatedAtUtc)
            }));

        index.Should().NotBeNull();
    }
}
