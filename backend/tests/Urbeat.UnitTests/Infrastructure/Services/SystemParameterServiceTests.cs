using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Services;

namespace Urbeat.UnitTests.Infrastructure.Services;

public sealed class SystemParameterServiceTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly SystemParameterService _sut;

    public SystemParameterServiceTests()
    {
        var databaseName = $"urbeat-system-parameters-{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));

        _provider = services.BuildServiceProvider();
        _sut = new SystemParameterService(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            _provider.GetRequiredService<ILogger<SystemParameterService>>());
    }

    public void Dispose()
    {
        _sut.Dispose();
        _provider.Dispose();
    }

    [Fact]
    public async Task GetBusinessAsync_ShouldOnlyReturnBusinessCatalogKeys()
    {
        await SeedAsync(
            new SystemParameter
            {
                Key = "Store.DefaultDeliveryFee",
                Value = "19.90",
                Type = SystemParameterType.Decimal,
                Group = "Business"
            },
            new SystemParameter
            {
                Key = "Subscription.DueSoonDays",
                Value = "3",
                Type = SystemParameterType.Int32,
                Group = "Business"
            },
            new SystemParameter
            {
                Key = "Jwt.ExpirationMinutes",
                Value = "15",
                Type = SystemParameterType.Int32,
                Group = "Security"
            },
            new SystemParameter
            {
                Key = "Email.SmtpPort",
                Value = "587",
                Type = SystemParameterType.Int32,
                Group = "Email"
            },
            new SystemParameter
            {
                Key = "Custom.BusinessExtra",
                Value = "rogue",
                Type = SystemParameterType.String,
                Group = "Business"
            });

        var parameters = await _sut.GetBusinessAsync();

        parameters.Select(x => x.Key).Should().BeEquivalentTo(
        [
            "Store.DefaultDeliveryFee",
            "Subscription.DueSoonDays"
        ]);
        parameters.Should().OnlyContain(x => x.Group == "Business");
        parameters.Should().NotContain(x => x.Key == "Custom.BusinessExtra");
    }

    [Theory]
    [InlineData("Jwt.ExpirationMinutes")]
    [InlineData("Email.SmtpPort")]
    [InlineData("Url.Asaas")]
    public async Task GetBusinessByKeyAsync_ShouldReturnNullForTechnicalKeys(string key)
    {
        await SeedAsync(new SystemParameter
        {
            Key = key,
            Value = "value",
            Type = SystemParameterType.String,
            Group = "Security"
        });

        var parameter = await _sut.GetBusinessByKeyAsync(key);

        parameter.Should().BeNull();
    }

    [Fact]
    public async Task GetBusinessByKeyAsync_ShouldReturnBusinessKeys()
    {
        await SeedAsync(new SystemParameter
        {
            Key = "Order.CodePrefix",
            Value = "URB-",
            Type = SystemParameterType.String,
            Group = "Business"
        });

        var parameter = await _sut.GetBusinessByKeyAsync("Order.CodePrefix");

        parameter.Should().NotBeNull();
        parameter!.Value.Should().Be("URB-");
    }

    [Fact]
    public async Task GetAllAsync_ShouldExposeMetadataAndMaskSecretValues()
    {
        await SeedAsync(
            new SystemParameter
            {
                Key = "Store.DefaultDeliveryFee",
                Value = "19.90",
                Type = SystemParameterType.Decimal,
                Group = "Business",
                Description = "Taxa de entrega padrão"
            },
            new SystemParameter
            {
                Key = "Payment.ApiSecret",
                Value = "super-secret-value",
                Type = SystemParameterType.String,
                Group = "Payment"
            });

        var parameters = await _sut.GetAllAsync();

        var fee = parameters.Single(x => x.Key == "Store.DefaultDeliveryFee");
        fee.Display.Should().Be("currency");
        fee.Value.Should().Be("19.90");
        fee.MaxLength.Should().Be(18);
        fee.MinValue.Should().Be(0m);
        fee.MaxValue.Should().Be(1_000_000m);
        fee.RequiresConfirmation.Should().BeFalse();
        fee.IsSecret.Should().BeFalse();

        var secret = parameters.Single(x => x.Key == "Payment.ApiSecret");
        secret.Value.Should().Be("********");
        secret.IsSecret.Should().BeTrue();
        secret.IsReadOnly.Should().BeTrue();
        secret.RequiresConfirmation.Should().BeTrue();
    }

    [Fact]
    public async Task GetByKeyAsync_ShouldReturnNullForUnknownKeys()
    {
        var parameter = await _sut.GetByKeyAsync("Missing.Parameter");

        parameter.Should().BeNull();
    }

    [Fact]
    public async Task SetValueAsync_ShouldPersistAndRefreshTheCacheImmediately()
    {
        await SeedAsync(new SystemParameter
        {
            Key = "Store.DefaultDeliveryFee",
            Value = "0",
            Type = SystemParameterType.Decimal,
            Group = "Business"
        });

        await _sut.SetValueAsync("Store.DefaultDeliveryFee", "25.50");

        (await _sut.GetValueAsync("Store.DefaultDeliveryFee")).Should().Be("25.50");

        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persisted = await db.SystemParameters.SingleAsync(x => x.Key == "Store.DefaultDeliveryFee");
        persisted.Value.Should().Be("25.50");
        persisted.UpdatedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task ReloadAllAsync_ShouldReplaceCachedValuesWithDatabaseValues()
    {
        await SeedAsync(new SystemParameter
        {
            Key = "Store.DefaultDeliveryFee",
            Value = "10.00",
            Type = SystemParameterType.Decimal,
            Group = "Business"
        });

        (await _sut.GetValueAsync("Store.DefaultDeliveryFee")).Should().Be("10.00");

        await using (var scope = _provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var entity = await db.SystemParameters.SingleAsync(x => x.Key == "Store.DefaultDeliveryFee");
            entity.Value = "12.00";
            await db.SaveChangesAsync();
        }

        await _sut.ReloadAllAsync();

        (await _sut.GetValueAsync("Store.DefaultDeliveryFee")).Should().Be("12.00");
    }

    [Fact]
    public async Task DeleteAsync_ShouldRemoveTheParameterFromTheCache()
    {
        await SeedAsync(new SystemParameter
        {
            Key = "Store.DefaultDeliveryFee",
            Value = "10.00",
            Type = SystemParameterType.Decimal,
            Group = "Business"
        });

        await _sut.DeleteAsync("Store.DefaultDeliveryFee");
        await _sut.ReloadAllAsync();

        (await _sut.GetValueAsync("Store.DefaultDeliveryFee")).Should().BeEmpty();
    }

    private async Task SeedAsync(params SystemParameter[] parameters)
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.SystemParameters.AddRange(parameters);
        await db.SaveChangesAsync();
    }
}
