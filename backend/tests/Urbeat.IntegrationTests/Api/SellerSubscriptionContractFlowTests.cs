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
using Microsoft.EntityFrameworkCore;
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
    public async Task Contract_ShouldUpgradeToSuperiorPlanAndCreateAwaitingGatewayStub()
    {
        var sellerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (sellerToken, sellerUserId, storeId) = await RegisterLoginAndCreateStoreAsync(sellerClient, "Pizza");
        sellerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sellerToken);

        var adminClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var adminToken = await LoginAdminAsync(adminClient);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var plan = await CreatePlanAsync(adminClient, "Plano Pro", 59.90m, isActive: true, level: 5);

        var contractResponse = await sellerClient.PostAsJsonAsync("/api/subscriptions/contract", new ContractSellerSubscriptionRequestDto
        {
            StoreId = storeId,
            PlanId = plan.Id,
            BillingPeriod = SubscriptionBillingPeriod.Monthly,
            FirstDueDateUtc = DateTime.UtcNow.AddDays(7)
        });

        contractResponse.StatusCode.Should().Be(
            HttpStatusCode.Created,
            await contractResponse.Content.ReadAsStringAsync());
        var contract = await contractResponse.Content.ReadFromJsonAsync<ContractSellerSubscriptionResponseDto>();
        contract.Should().NotBeNull();
        contract!.StoreId.Should().Be(storeId);
        contract.SellerUserId.Should().Be(sellerUserId);
        contract.GatewayCustomerId.Should().BeEmpty();
        contract.GatewaySubscriptionId.Should().BeEmpty();
        contract.AwaitingGateway.Should().BeTrue();
        contract.Status.Should().Be(SellerSubscriptionBillingStatus.Active);
        contract.BillingPeriod.Should().Be(SubscriptionBillingPeriod.Monthly);
        contract.PlanAmount.Should().Be(59.90m);

        var mySubscription = await sellerClient.GetFromJsonAsync<SellerSubscriptionMyResponseDto>("/api/subscriptions/my");
        mySubscription.Should().NotBeNull();
        mySubscription!.HasSubscription.Should().BeTrue();
        mySubscription.StoreId.Should().Be(storeId);
        mySubscription.PlanName.Should().Be("Plano Pro");
        mySubscription.PlanAmount.Should().Be(59.90m);
        mySubscription.NextChargeAmount.Should().Be(59.90m);
        mySubscription.HasGatewaySubscription.Should().BeFalse();
        mySubscription.HasPendingGatewayCharge.Should().BeTrue();
        mySubscription.IsTrialActive.Should().BeFalse();
        mySubscription.BillingStatus.Should().Be(SellerSubscriptionBillingStatus.Active);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persisted = dbContext.SellerSubscriptions.SingleOrDefault(x => x.StoreId == storeId);
        persisted.Should().NotBeNull();
        persisted!.GatewayCustomerId.Should().BeEmpty();
        persisted.GatewaySubscriptionId.Should().BeEmpty();
        persisted.PlanLevelSnapshot.Should().Be(5);
        dbContext.SellerSubscriptionCharges.Count(x => x.StoreId == storeId).Should().Be(2);
    }

    [Fact]
    public async Task Contract_ShouldReturnConflict_WhenStoreAlreadyHasTheSamePlan()
    {
        var sellerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (sellerToken, _, storeId) = await RegisterLoginAndCreateStoreAsync(sellerClient, "Pizza");
        sellerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sellerToken);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var currentPlanId = dbContext.SellerSubscriptions.Single(x => x.StoreId == storeId).PlanId!.Value;

        var response = await sellerClient.PostAsJsonAsync("/api/subscriptions/contract", new ContractSellerSubscriptionRequestDto
        {
            StoreId = storeId,
            PlanId = currentPlanId,
            BillingPeriod = SubscriptionBillingPeriod.Monthly
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
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
        var inactivePlan = await CreatePlanAsync(adminClient, "Plano Inativo", 29.90m, isActive: false, level: 6);

        var response = await sellerClient.PostAsJsonAsync("/api/subscriptions/contract", new ContractSellerSubscriptionRequestDto
        {
            StoreId = storeId,
            PlanId = inactivePlan.Id,
            FirstDueDateUtc = DateTime.UtcNow.AddDays(5)
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Contract_ShouldRejectDowngrade()
    {
        var sellerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (sellerToken, _, storeId) = await RegisterLoginAndCreateStoreAsync(sellerClient, "Pizza");
        sellerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sellerToken);

        var adminClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var adminToken = await LoginAdminAsync(adminClient);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var lowerPlan = await CreatePlanAsync(adminClient, "Plano Inferior", 9.90m, isActive: true, level: 0);

        var response = await sellerClient.PostAsJsonAsync("/api/subscriptions/contract", new ContractSellerSubscriptionRequestDto
        {
            StoreId = storeId,
            PlanId = lowerPlan.Id
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ListActivePlans_ShouldReturnOnlyActivePlans()
    {
        var sellerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (sellerToken, _, _) = await RegisterLoginAndCreateStoreAsync(sellerClient, "Pizza");
        sellerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sellerToken);

        var adminClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var adminToken = await LoginAdminAsync(adminClient);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var activePlan = await CreatePlanAsync(adminClient, $"Plano Ativo {Guid.NewGuid():N}", 39.90m, isActive: true);
        var inactivePlan = await CreatePlanAsync(adminClient, $"Plano Inativo {Guid.NewGuid():N}", 29.90m, isActive: false);

        var plans = await sellerClient.GetFromJsonAsync<List<PlanResponseDto>>("/api/subscriptions/plans");

        plans.Should().NotBeNull();
        plans!.Should().OnlyContain(plan => plan.IsActive);
        plans.Should().Contain(plan => plan.Id == activePlan.Id);
        plans.Should().NotContain(plan => plan.Id == inactivePlan.Id);
    }

    [Fact]
    public async Task Contract_ShouldReturnForbidden_WhenStoreBelongsToAnotherSeller()
    {
        var sellerAClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (_, _, storeId) = await RegisterLoginAndCreateStoreAsync(sellerAClient, "Pizza");

        var sellerBClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (sellerBToken, _, _) = await RegisterLoginAndCreateStoreAsync(sellerBClient, "Burger");
        sellerBClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sellerBToken);

        var adminClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var adminToken = await LoginAdminAsync(adminClient);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var plan = await CreatePlanAsync(adminClient, $"Plano Ownership {Guid.NewGuid():N}", 49.90m, isActive: true, level: 7);

        var response = await sellerBClient.PostAsJsonAsync("/api/subscriptions/contract", new ContractSellerSubscriptionRequestDto
        {
            StoreId = storeId,
            PlanId = plan.Id,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Contract_ShouldIgnoreClientSuppliedPriceAndDuration()
    {
        var sellerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (sellerToken, _, storeId) = await RegisterLoginAndCreateStoreAsync(sellerClient, "Pizza");
        sellerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sellerToken);

        var adminClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var adminToken = await LoginAdminAsync(adminClient);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var plan = await CreatePlanAsync(adminClient, $"Plano Preco {Guid.NewGuid():N}", 59.90m, isActive: true, level: 8);

        var response = await sellerClient.PostAsJsonAsync("/api/subscriptions/contract", new
        {
            StoreId = storeId,
            PlanId = plan.Id,
            BillingPeriod = SubscriptionBillingPeriod.Monthly,
            FirstDueDateUtc = DateTime.UtcNow.AddDays(1),
            PlanAmount = 1.00m,
            TrialDays = 999
        });

        response.StatusCode.Should().Be(
            HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync());
        var contract = await response.Content.ReadFromJsonAsync<ContractSellerSubscriptionResponseDto>();
        contract.Should().NotBeNull();
        contract!.PlanAmount.Should().Be(59.90m);
        // Upgrade não concede novo trial: a cobrança vence imediatamente.
        contract.NextBillingDateUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(2));

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persisted = dbContext.SellerSubscriptions.Single(x => x.StoreId == storeId);
        persisted.PlanAmount.Should().Be(59.90m);
    }

    private static async Task<PlanResponseDto> CreatePlanAsync(
        HttpClient adminClient,
        string name,
        decimal amount,
        bool isActive,
        int level = 0)
    {
        var response = await adminClient.PostAsJsonAsync("/api/admin/plans", new CreatePlanRequestDto
        {
            Name = name,
            MonthlyAmount = amount,
            Description = $"{name} description",
            IsActive = isActive,
            Level = level
        });

        response.StatusCode.Should().Be(
            HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync());
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

        await _factory.RegisterSellerAsync(client, email, password, "Subscription Contract Seller", "11980001111");

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
            Slug = $"loja-contract-{Guid.NewGuid():N}",
            PhoneNumber = "11987771111",
            CuisineType = cuisineType,
            MaxDeliveryRadiusKm = 5,
        });

        createStoreResponse.StatusCode.Should().Be(
            HttpStatusCode.Created,
            await createStoreResponse.Content.ReadAsStringAsync());
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
