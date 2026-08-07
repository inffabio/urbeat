using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Urbeat.IntegrationTests.Api;

public sealed class AsaasSubscriptionWebhookFlowTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public AsaasSubscriptionWebhookFlowTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AsaasWebhook_ShouldRequireValidToken()
    {
        var webhookClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var payload = "{\"event\":\"PAYMENT_OVERDUE\",\"payment\":{\"id\":\"pay_1\",\"status\":\"OVERDUE\"}}";

        var response = await webhookClient.PostAsync(
            "/api/webhooks/asaas",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AsaasWebhook_ShouldBeIdempotentAndUpdateSellerSubscriptionStatus()
    {
        var sellerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (sellerToken, sellerUserId) = await RegisterAndLoginSellerAsync(sellerClient);
        sellerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sellerToken);

        var webhookPayload = $"{{\"id\":\"evt_same_001\",\"event\":\"PAYMENT_OVERDUE\",\"sellerUserId\":\"{sellerUserId}\",\"payment\":{{\"id\":\"pay_001\",\"status\":\"OVERDUE\",\"dueDate\":\"2026-05-01\"}}}}";

        var webhookClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        webhookClient.DefaultRequestHeaders.Add("asaas-access-token", "test-asaas-token");

        var firstResponse = await webhookClient.PostAsync(
            "/api/webhooks/asaas",
            new StringContent(webhookPayload, Encoding.UTF8, "application/json"));

        var secondResponse = await webhookClient.PostAsync(
            "/api/webhooks/asaas",
            new StringContent(webhookPayload, Encoding.UTF8, "application/json"));

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var subscriptionResponse = await sellerClient.GetAsync("/api/subscriptions/my");
        subscriptionResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var subscription = await subscriptionResponse.Content.ReadFromJsonAsync<SellerSubscriptionMyResponseDto>();
        subscription.Should().NotBeNull();
        subscription!.HasSubscription.Should().BeTrue();
        subscription.BillingStatus.Should().Be(SellerSubscriptionBillingStatus.Overdue);

        var chargesResponse = await sellerClient.GetAsync("/api/subscriptions/my/charges");
        chargesResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var charges = await chargesResponse.Content.ReadFromJsonAsync<List<SellerSubscriptionChargeHistoryItemDto>>();
        charges.Should().NotBeNull();
        charges!.Should().ContainSingle(x => x.GatewayChargeId == "pay_001" && x.BillingStatus == SellerSubscriptionBillingStatus.Overdue);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var eventsCount = dbContext.SubscriptionWebhookEvents.Count(x => x.EventKey == "asaas:evt_same_001");
        eventsCount.Should().Be(1);
    }

    [Fact]
    public async Task AsaasWebhook_ShouldUpdateExistingChargeCycle_WhenPaymentStatusChanges()
    {
        var sellerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (sellerToken, sellerUserId) = await RegisterAndLoginSellerAsync(sellerClient);
        sellerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sellerToken);

        var webhookClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        webhookClient.DefaultRequestHeaders.Add("asaas-access-token", "test-asaas-token");

        var overduePayload = $"{{\"id\":\"evt_overdue_001\",\"event\":\"PAYMENT_OVERDUE\",\"sellerUserId\":\"{sellerUserId}\",\"payment\":{{\"id\":\"pay_cycle_01\",\"status\":\"OVERDUE\",\"dueDate\":\"2026-05-01\",\"value\":59.90}}}}";
        var paidPayload = $"{{\"id\":\"evt_paid_001\",\"event\":\"PAYMENT_RECEIVED\",\"sellerUserId\":\"{sellerUserId}\",\"payment\":{{\"id\":\"pay_cycle_01\",\"status\":\"RECEIVED\",\"dueDate\":\"2026-05-01\",\"paymentDate\":\"2026-05-02T10:20:00Z\",\"value\":59.90}}}}";

        var overdueResponse = await webhookClient.PostAsync(
            "/api/webhooks/asaas",
            new StringContent(overduePayload, Encoding.UTF8, "application/json"));
        overdueResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var paidResponse = await webhookClient.PostAsync(
            "/api/webhooks/asaas",
            new StringContent(paidPayload, Encoding.UTF8, "application/json"));
        paidResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var chargesResponse = await sellerClient.GetAsync("/api/subscriptions/my/charges");
        chargesResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var charges = await chargesResponse.Content.ReadFromJsonAsync<List<SellerSubscriptionChargeHistoryItemDto>>();
        charges.Should().NotBeNull();
        charges!.Should().ContainSingle(x =>
            x.GatewayChargeId == "pay_cycle_01"
            && x.BillingStatus == SellerSubscriptionBillingStatus.Active
            && x.GatewayStatus == "RECEIVED"
            && x.Amount == 59.90m
            && x.PaidAtUtc.HasValue);
    }

    private async Task<(string AccessToken, Guid SellerUserId)> RegisterAndLoginSellerAsync(HttpClient client)
    {
        var email = $"asaas.seller.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = "Asaas Seller",
            Email = email,
            Password = password,
            PhoneNumber = "11983334444"
        });
        await _factory.ConfirmEmailAsync(email);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login/seller", new LoginRequestDto
        {
            Email = email,
            Password = password
        });

        var token = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        token.Should().NotBeNull();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
        var createStoreResponse = await client.PostAsJsonAsync("/api/stores", new CreateStoreRequestDto
        {
            Name = "Loja Asaas",
            PhoneNumber = "11989990000",
            Description = "Loja para testes de webhook Asaas",
            CuisineType = "Pizza",
            MaxDeliveryRadiusKm = 5,
        });
        createStoreResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var sellerUserId = GetUserIdFromAccessToken(token.AccessToken);
        return (token.AccessToken, sellerUserId);
    }

    private static Guid GetUserIdFromAccessToken(string accessToken)
    {
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        var subject = jwt.Claims.FirstOrDefault(x => x.Type == JwtRegisteredClaimNames.Sub)?.Value
            ?? jwt.Claims.FirstOrDefault(x => x.Type == ClaimTypes.NameIdentifier)?.Value;

        if (Guid.TryParse(subject, out var userId))
        {
            return userId;
        }

        throw new InvalidOperationException("Access token does not contain a valid seller user id.");
    }
}
