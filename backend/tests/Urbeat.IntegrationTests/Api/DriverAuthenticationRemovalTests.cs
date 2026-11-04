using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Urbeat.Application.DTOs;
using Urbeat.IntegrationTests.Infrastructure;

namespace Urbeat.IntegrationTests.Api;

/// <summary>
/// The motoboy operates exclusively through public bearer links (<c>/rota/:token</c> and
/// <c>/entrega/:token</c>). There is no driver account, password, JWT role or authenticated
/// driver endpoint; the seller manages the operational link and creates the public links.
/// </summary>
public sealed class DriverAuthenticationRemovalTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public DriverAuthenticationRemovalTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task LoginDriver_ShouldNotExist()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.PostAsJsonAsync("/api/auth/login/driver", new LoginRequestDto
        {
            Email = "driver@urbeat.local",
            Password = "SenhaForte123"
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("GET", "/api/driver/stores")]
    [InlineData("GET", "/api/driver/orders")]
    [InlineData("GET", "/api/driver/orders/0197f0f0-0000-7000-8000-000000000000")]
    [InlineData("POST", "/api/driver/orders/0197f0f0-0000-7000-8000-000000000000/start")]
    [InlineData("POST", "/api/driver/orders/0197f0f0-0000-7000-8000-000000000000/confirm-pin")]
    [InlineData("POST", "/api/driver/orders/0197f0f0-0000-7000-8000-000000000000/handover")]
    public async Task AuthenticatedDriverEndpoints_ShouldNotExist(string method, string url)
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DriverInviteEndpoints_ShouldNotExist()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var preview = await client.GetAsync("/api/delivery-drivers/invites/any-token");
        var accept = await client.PostAsJsonAsync(
            "/api/delivery-drivers/invites/any-token/accept",
            new { password = "SenhaForte123" });
        var legacyInvite = await client.PostAsJsonAsync(
            "/api/delivery-drivers/store/invite",
            new { fullName = "Entregador", email = "driver@urbeat.local", phoneNumber = "11999999999" });

        preview.StatusCode.Should().Be(HttpStatusCode.NotFound);
        accept.StatusCode.Should().Be(HttpStatusCode.NotFound);
        legacyInvite.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeliveryDriverOnlyPolicy_ShouldNotBeRegistered()
    {
        using var scope = _factory.Services.CreateScope();
        var policyProvider = scope.ServiceProvider.GetRequiredService<IAuthorizationPolicyProvider>();

        var policy = await policyProvider.GetPolicyAsync("DeliveryDriverOnly");

        policy.Should().BeNull("the authenticated motoboy role was replaced by public bearer links");
    }

    [Fact]
    public async Task PublicDeliveryLinks_ShouldRemainAnonymousAndTokenized()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var route = await client.GetAsync("/api/public/delivery-routes/not-a-real-token");
        var delivery = await client.GetAsync("/api/public/deliveries/not-a-real-token");

        route.StatusCode.Should().Be(HttpStatusCode.NotFound);
        delivery.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
