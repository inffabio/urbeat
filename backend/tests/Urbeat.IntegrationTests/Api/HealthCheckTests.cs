using System.Net;
using FluentAssertions;
using Urbeat.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Urbeat.IntegrationTests.Api;

public sealed class HealthCheckTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _httpClient;

    public HealthCheckTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _httpClient = factory.CreateClient();
    }

    [Fact]
    public async Task GetHealth_ShouldReturnOk()
    {
        var response = await _httpClient.GetAsync("/health");

        response.IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact]
    public async Task GetHealthLive_ShouldReturnOk_WithoutCheckingDependencies()
    {
        var response = await _httpClient.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetHealthReady_ShouldReturnOk_WhenDependenciesAreAvailable()
    {
        var response = await _httpClient.GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthEndpoints_ShouldIgnoreOperationsTaggedChecks()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddHealthChecks()
                    .AddCheck(
                        "synthetic-operations-dependency",
                        () => HealthCheckResult.Unhealthy("synthetic operations failure"),
                        tags: new[] { "operations" });
            });
        });

        var client = factory.CreateClient();

        // The admin operations probes must never change the container/legacy health contract.
        (await client.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthEndpoints_ShouldPreserveDependencyFailureSemantics()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddHealthChecks()
                    .AddCheck(
                        "synthetic-readiness-dependency",
                        () => HealthCheckResult.Unhealthy("synthetic dependency failure"),
                        tags: new[] { "ready" });
            });
        });

        var client = factory.CreateClient();

        // Liveness intentionally ignores dependency state.
        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Readiness and the legacy endpoint keep detecting the unavailable dependency.
        (await client.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await client.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }
}
