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

    [Fact]
    public void Order_ShouldMarkStatusVersionAsConcurrencyToken()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-model-{Guid.NewGuid()}")
            .Options;
        using var db = new ApplicationDbContext(options);

        var statusVersion = db.Model.FindEntityType(typeof(Order))!
            .FindProperty(nameof(Order.StatusVersion))!;

        statusVersion.IsConcurrencyToken.Should().BeTrue();
    }

    [Fact]
    public void SystemParameter_ShouldKeepValueUnboundedAndDocumentStringMetadataLimits()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-model-{Guid.NewGuid()}")
            .Options;
        using var db = new ApplicationDbContext(options);

        var entityType = db.Model.FindEntityType(typeof(SystemParameter));
        entityType.Should().NotBeNull();

        entityType!.FindProperty(nameof(SystemParameter.Key))!.GetMaxLength().Should().Be(200);
        entityType.FindProperty(nameof(SystemParameter.Description))!.GetMaxLength().Should().Be(500);
        entityType.FindProperty(nameof(SystemParameter.Group))!.GetMaxLength().Should().Be(100);
        entityType.FindProperty(nameof(SystemParameter.Type))!.GetMaxLength().Should().Be(20);
        entityType.FindProperty(nameof(SystemParameter.Value))!.GetMaxLength().Should().BeNull();
    }

    [Fact]
    public void UserRoleCredential_ShouldHaveUniqueIndexPerUserAndRole()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-model-{Guid.NewGuid()}")
            .Options;
        using var db = new ApplicationDbContext(options);

        var entityType = db.Model.FindEntityType(typeof(UserRoleCredential));
        entityType.Should().NotBeNull();

        var index = entityType!.GetIndexes().SingleOrDefault(x =>
            x.Properties.Select(property => property.Name).SequenceEqual(new[]
            {
                nameof(UserRoleCredential.UserId),
                nameof(UserRoleCredential.Role)
            }));

        index.Should().NotBeNull();
        index!.IsUnique.Should().BeTrue();
        entityType.FindProperty(nameof(UserRoleCredential.PasswordHash))!.GetMaxLength().Should().Be(500);
    }

    [Fact]
    public void DeliveryNeighborhood_ShouldNotDefineGlobalNeighborhoodCityUniqueIndex_AndShouldKeepCityIdNormalizedNameUniqueIndex()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-model-{Guid.NewGuid()}")
            .Options;
        using var db = new ApplicationDbContext(options);

        var entityType = db.Model.FindEntityType(typeof(DeliveryNeighborhood));
        var indexes = entityType!.GetIndexes();

        var globalIndex = indexes.SingleOrDefault(x =>
            x.Properties.Select(property => property.Name).SequenceEqual(new[]
            {
                nameof(DeliveryNeighborhood.Neighborhood),
                nameof(DeliveryNeighborhood.City)
            }));

        var cityNormalizedIndex = indexes.SingleOrDefault(x =>
            x.Properties.Select(property => property.Name).SequenceEqual(new[]
            {
                nameof(DeliveryNeighborhood.CityId),
                nameof(DeliveryNeighborhood.NormalizedName)
            }));

        globalIndex.Should().BeNull();
        cityNormalizedIndex.Should().NotBeNull();
        cityNormalizedIndex!.IsUnique.Should().BeTrue();
    }
}
