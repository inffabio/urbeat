using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Urbeat.WebApi.DependencyInjection;

namespace Urbeat.UnitTests.WebApi;

public sealed class SignalRRedisBackplaneTests
{
    private static IConfiguration BuildConfiguration(string? redisConnectionString, bool? enabled = null)
    {
        var values = new Dictionary<string, string?>();
        if (redisConnectionString is not null)
        {
            values["Redis:ConnectionString"] = redisConnectionString;
        }

        if (enabled.HasValue)
        {
            values["SignalR:RedisBackplaneEnabled"] = enabled.Value.ToString().ToLowerInvariant();
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static Mock<IWebHostEnvironment> BuildEnvironment(string environmentName)
    {
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(x => x.EnvironmentName).Returns(environmentName);
        return environment;
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public void ShouldEnable_ShouldReturnTrue_WhenExplicitlyEnabledAndConnectionStringIsConfigured(string environmentName)
    {
        var configuration = BuildConfiguration("localhost:6379", enabled: true);
        var environment = BuildEnvironment(environmentName);

        SignalRRedisBackplane.ShouldEnable(configuration, environment.Object).Should().BeTrue();
    }

    [Fact]
    public void ShouldEnable_ShouldReturnFalse_WhenConnectionStringIsMissing()
    {
        SignalRRedisBackplane.ShouldEnable(BuildConfiguration(null, enabled: true), BuildEnvironment("Production").Object).Should().BeFalse();
    }

    [Fact]
    public void ShouldEnable_ShouldReturnFalse_WhenConnectionStringIsBlank()
    {
        SignalRRedisBackplane.ShouldEnable(BuildConfiguration("   ", enabled: true), BuildEnvironment("Production").Object).Should().BeFalse();
    }

    [Fact]
    public void ShouldEnable_ShouldReturnFalse_WhenFlagIsMissing_EvenWithConnectionString()
    {
        SignalRRedisBackplane.ShouldEnable(BuildConfiguration("localhost:6379", enabled: null), BuildEnvironment("Production").Object).Should().BeFalse();
    }

    [Fact]
    public void ShouldEnable_ShouldReturnFalse_WhenExplicitlyDisabled()
    {
        SignalRRedisBackplane.ShouldEnable(BuildConfiguration("localhost:6379", enabled: false), BuildEnvironment("Production").Object).Should().BeFalse();
    }

    [Fact]
    public void ShouldEnable_ShouldReturnFalse_InTestingEnvironment()
    {
        SignalRRedisBackplane.ShouldEnable(BuildConfiguration("localhost:6379", enabled: true), BuildEnvironment("Testing").Object).Should().BeFalse();
    }

    [Fact]
    public void AddWebApi_ShouldRegisterRedisBackplane_WhenExplicitlyEnabled()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddWebApi(BuildConfiguration("localhost:6379", enabled: true), BuildEnvironment("Production").Object);

        var hasRedisBackplane = services.Any(descriptor =>
            descriptor.ServiceType.IsGenericType
            && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(HubLifetimeManager<>)
            && descriptor.ImplementationType is not null
            && descriptor.ImplementationType.Name.StartsWith("RedisHubLifetimeManager", System.StringComparison.Ordinal));

        hasRedisBackplane.Should().BeTrue();
    }

    [Fact]
    public void AddWebApi_ShouldNotRegisterRedisBackplane_WhenDisabled()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddWebApi(BuildConfiguration(null), BuildEnvironment("Production").Object);

        var hasRedisBackplane = services.Any(descriptor =>
            descriptor.ImplementationType is not null
            && descriptor.ImplementationType.Name.StartsWith("RedisHubLifetimeManager", System.StringComparison.Ordinal));

        hasRedisBackplane.Should().BeFalse();
    }

    [Fact]
    public void AddWebApi_ShouldNotRegisterRedisBackplane_WhenFlagIsMissing()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddWebApi(BuildConfiguration("localhost:6379"), BuildEnvironment("Production").Object);

        var hasRedisBackplane = services.Any(descriptor =>
            descriptor.ImplementationType is not null
            && descriptor.ImplementationType.Name.StartsWith("RedisHubLifetimeManager", System.StringComparison.Ordinal));

        hasRedisBackplane.Should().BeFalse();
    }
}
