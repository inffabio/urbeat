using FluentAssertions;
using Urbeat.IntegrationTests.Infrastructure;

namespace Urbeat.IntegrationTests.Api;

public sealed class HealthCheckTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _httpClient;

    public HealthCheckTests(TestWebApplicationFactory factory)
    {
        _httpClient = factory.CreateClient();
    }

    [Fact]
    public async Task GetHealth_ShouldReturnOk()
    {
        var response = await _httpClient.GetAsync("/health");

        response.IsSuccessStatusCode.Should().BeTrue();
    }
}
