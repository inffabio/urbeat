using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Urbeat.Application.Interfaces;
using Urbeat.Infrastructure.Realtime;
using Urbeat.Infrastructure.Realtime.Hubs;
using Urbeat.Worker;

namespace Urbeat.UnitTests.Worker;

/// <summary>
/// Regression for the production outage: the outbox runs in Urbeat.Worker (Hangfire Role Business),
/// while the live SignalR publisher used to be registered by the WebApi composition only. The worker
/// then processed the outbox without publishing, and both the seller dashboard and the storefront
/// tracking stayed stale until a manual refresh.
/// </summary>
public sealed class WorkerHostCompositionTests
{
    [Fact]
    public void Worker_host_should_resolve_the_live_notification_publisher()
    {
        using var provider = BuildProvider("Production", "redis:6379", backplaneEnabled: true);
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ILiveNotificationPublisher>()
            .Should().BeOfType<SignalRLiveNotificationPublisher>();
        scope.ServiceProvider.GetRequiredService<IHubContext<SellerNotificationHub>>()
            .Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IHubContext<CustomerNotificationHub>>()
            .Should().NotBeNull();
    }

    [Fact]
    public void Worker_host_should_register_the_redis_backplane_when_enabled()
    {
        var services = BuildServices("Production", "redis:6379", backplaneEnabled: true);

        RedisBackplaneDescriptors(services).Should().NotBeEmpty(
            "the worker must publish into the same backplane as the WebApi replicas");
    }

    [Fact]
    public void Worker_host_should_stay_in_process_when_the_flag_is_missing()
    {
        var services = BuildServices("Production", "redis:6379", backplaneEnabled: null);

        RedisBackplaneDescriptors(services).Should().BeEmpty();
        // Missing configuration must not break the host: the publisher is still registered and the
        // in-process hub lifetime manager keeps single-host deployments working.
        using var provider = BuildProvider("Production", "redis:6379", backplaneEnabled: null);
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ILiveNotificationPublisher>()
            .Should().BeOfType<SignalRLiveNotificationPublisher>();
    }

    [Fact]
    public void Worker_host_should_stay_in_process_in_the_testing_environment()
    {
        var services = BuildServices("Testing", "redis:6379", backplaneEnabled: true);

        RedisBackplaneDescriptors(services).Should().BeEmpty(
            "the Testing environment must never attempt a real Redis connection");
    }

    [Fact]
    public async Task Worker_host_should_publish_through_the_real_signalr_hub_contexts()
    {
        // Testing keeps the in-process hub lifetime manager, so this exercises the real
        // AddSignalR registrations the worker now composes (no Redis required) and proves the
        // publisher resolves and completes for all three event names.
        using var provider = BuildProvider("Testing", "redis:6379", backplaneEnabled: true);
        using var scope = provider.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<ILiveNotificationPublisher>();

        (await publisher.PublishSellerAsync(
                Guid.NewGuid(), "ReceiveSellerNotification", new { orderId = Guid.NewGuid() }))
            .Should().BeTrue();
        (await publisher.PublishCustomerAsync(
                Guid.NewGuid(), "OrderStatusUpdated", new { orderId = Guid.NewGuid() }))
            .Should().BeTrue();
        (await publisher.PublishCustomerAsync(
                Guid.NewGuid(), "ReceiveCustomerNotification", new { orderId = Guid.NewGuid() }))
            .Should().BeTrue();
    }

    private static IServiceCollection BuildServices(
        string environmentName,
        string? redisConnectionString,
        bool? backplaneEnabled)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddUrbeatWorkerHost(BuildConfiguration(redisConnectionString, backplaneEnabled), new TestEnvironment(environmentName));
        return services;
    }

    private static ServiceProvider BuildProvider(
        string environmentName,
        string? redisConnectionString,
        bool? backplaneEnabled)
    {
        return BuildServices(environmentName, redisConnectionString, backplaneEnabled).BuildServiceProvider();
    }

    private static IReadOnlyList<ServiceDescriptor> RedisBackplaneDescriptors(IServiceCollection services) =>
        services
            .Where(descriptor => descriptor.ImplementationType is not null
                && descriptor.ImplementationType.Name.StartsWith("RedisHubLifetimeManager", StringComparison.Ordinal))
            .ToList();

    private static IConfiguration BuildConfiguration(string? redisConnectionString, bool? backplaneEnabled)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] =
                "Host=localhost;Port=5432;Database=urbeat;Username=urbeat;Password=urbeat",
            ["Hangfire:Role"] = "Business",
        };

        if (redisConnectionString is not null)
        {
            values["Redis:ConnectionString"] = redisConnectionString;
        }

        if (backplaneEnabled.HasValue)
        {
            values["SignalR:RedisBackplaneEnabled"] = backplaneEnabled.Value.ToString().ToLowerInvariant();
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public TestEnvironment(string environmentName) => EnvironmentName = environmentName;

        public string EnvironmentName { get; set; }

        public string ApplicationName { get; set; } = "Urbeat.UnitTests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
