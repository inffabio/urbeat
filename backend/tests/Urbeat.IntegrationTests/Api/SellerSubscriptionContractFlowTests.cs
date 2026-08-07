using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Urbeat.IntegrationTests.Api;

public sealed class SellerSubscriptionContractFlowTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public SellerSubscriptionContractFlowTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Contract_ShouldCreateGatewayIdentifiersAndActivateSubscription()
    {
        var sellerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (sellerToken, sellerUserId, storeId) = await RegisterLoginAndCreateStoreAsync(sellerClient, "Pizza");
        sellerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sellerToken);

        var adminClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var adminToken = await LoginAdminAsync(adminClient);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var plan = await CreatePlanAsync(adminClient, "Plano Pro", 59.90m, isActive: true);

        var contractResponse = await sellerClient.PostAsJsonAsync("/api/subscriptions/contract", new ContractSellerSubscriptionRequestDto
        {
            StoreId = storeId, 
            PlanId = plan.Id,
            FirstDueDateUtc = DateTime.UtcNow.AddDays(7)
        });

        contractResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var contract = await contractResponse.Content.ReadFromJsonAsync<ContractSellerSubscriptionResponseDto>();
        contract.Should().NotBeNull();
        contract!.StoreId.Should().Be(storeId);
        contract.SellerUserId.Should().Be(sellerUserId);
        contract.GatewayCustomerId.Should().NotBeNullOrWhiteSpace();
        contract.GatewaySubscriptionId.Should().NotBeNullOrWhiteSpace();
        contract.Status.Should().Be(SellerSubscriptionBillingStatus.Active);

        var mySubscription = await sellerClient.GetFromJsonAsync<SellerSubscriptionMyResponseDto>("/api/subscriptions/my");
        mySubscription.Should().NotBeNull();
        mySubscription!.HasSubscription.Should().BeTrue();
        mySubscription.PlanName.Should().Be("Plano Pro");
        mySubscription.PlanAmount.Should().Be(59.90m);
        mySubscription.BillingStatus.Should().Be(SellerSubscriptionBillingStatus.Active);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persisted = dbContext.SellerSubscriptions.SingleOrDefault(x => x.StoreId == storeId);
        persisted.Should().NotBeNull();
        persisted!.GatewayCustomerId.Should().Be(contract.GatewayCustomerId);
        persisted.GatewaySubscriptionId.Should().Be(contract.GatewaySubscriptionId);
    }

    [Fact]
    public async Task Contract_ShouldReturnConflict_WhenStoreAlreadyContracted()
    {
        var sellerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (sellerToken, _, storeId) = await RegisterLoginAndCreateStoreAsync(sellerClient, "Pizza");
        sellerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sellerToken);

        var adminClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var adminToken = await LoginAdminAsync(adminClient);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var plan = await CreatePlanAsync(adminClient, "Plano MVP", 39.90m, isActive: true);

        var request = new ContractSellerSubscriptionRequestDto
        {
            StoreId = storeId, 
            PlanId = plan.Id,
            FirstDueDateUtc = DateTime.UtcNow.AddDays(5)
        };

        var first = await sellerClient.PostAsJsonAsync("/api/subscriptions/contract", request);
        var second = await sellerClient.PostAsJsonAsync("/api/subscriptions/contract", request);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Contract_ShouldReturnBadRequest_WhenPlanIsInactive()
    {
        var sellerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (sellerToken, _, storeId) = await RegisterLoginAndCreateStoreAsync(sellerClient, "Pizza");
        sellerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sellerToken);

        var adminClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var adminToken = await LoginAdminAsync(adminClient);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var inactivePlan = await CreatePlanAsync(adminClient, "Plano Inativo", 29.90m, isActive: false);

        var response = await sellerClient.PostAsJsonAsync("/api/subscriptions/contract", new ContractSellerSubscriptionRequestDto
        {
            StoreId = storeId, 
            PlanId = inactivePlan.Id,
            FirstDueDateUtc = DateTime.UtcNow.AddDays(5)
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static async Task<PlanResponseDto> CreatePlanAsync(HttpClient adminClient, string name, decimal amount, bool isActive)
    {
        var response = await adminClient.PostAsJsonAsync("/api/admin/plans", new CreatePlanRequestDto
        {
            Name = name,
            Amount = amount,
            Description = $"{name} description",
            IsActive = isActive
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var plan = await response.Content.ReadFromJsonAsync<PlanResponseDto>();
        plan.Should().NotBeNull();
        return plan!;
    }

    private static async Task<string> LoginAdminAsync(HttpClient client)
    {
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login/admin", new LoginRequestDto
        {
            Email = "admin@urbeat.local",
            Password = "Admin12345"
        });

        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        token.Should().NotBeNull();
        return token!.AccessToken;
    }

    private async Task<(string AccessToken, Guid SellerUserId, Guid StoreId)> RegisterLoginAndCreateStoreAsync(HttpClient client, string cuisineType)
    {
        var email = $"subscription.contract.seller.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = "Subscription Contract Seller",
            Email = email,
            Password = password,
            PhoneNumber = "11980001111"
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
            Name = "Loja Contract",
            PhoneNumber = "11987771111",
            Description = "Loja para contrato assinatura",
            CuisineType = cuisineType,
            MaxDeliveryRadiusKm = 5,
        });

        createStoreResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var store = await createStoreResponse.Content.ReadFromJsonAsync<StoreResponseDto>();
        store.Should().NotBeNull();

        var sellerUserId = GetUserIdFromAccessToken(token.AccessToken);
        return (token.AccessToken, sellerUserId, store!.Id);
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
