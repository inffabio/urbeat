using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Urbeat.IntegrationTests.Api;

public sealed class SellerRegistrationSubscriptionTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public SellerRegistrationSubscriptionTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task RegisterSeller_ShouldRejectMissingPlanSelection()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"plan.missing.{Guid.NewGuid():N}@urbeat.local";

        var response = await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = "Sem Plano",
            Email = email,
            Password = "SenhaForte123",
            PhoneNumber = "11987770000"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("plano");
        await AssertNoUserAsync(email);
    }

    [Fact]
    public async Task RegisterSeller_ShouldRejectInactiveOrDisabledPeriodWithoutSideEffects()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"plan.invalid.{Guid.NewGuid():N}@urbeat.local";

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Plans.Add(new Plan
            {
                Name = $"Inativo {Guid.NewGuid():N}",
                Amount = 19.90m,
                IsActive = false,
                MonthlyEnabled = true
            });
            await db.SaveChangesAsync();
        }

        Guid inactivePlanId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            inactivePlanId = (await db.Plans.SingleAsync(x => x.Name.StartsWith("Inativo"))).Id;
        }

        var response = await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = "Plano Inativo",
            Email = email,
            Password = "SenhaForte123",
            PhoneNumber = "11987770000",
            PlanId = inactivePlanId,
            BillingPeriod = SubscriptionBillingPeriod.Monthly
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertNoUserAsync(email);
    }

    [Fact]
    public async Task RegisterSeller_ShouldCreateSelectionAndStoreCreation_ShouldSnapshotPlanAndCreateSingleStub()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"plan.flow.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        var planId = await _factory.GetDefaultPlanIdAsync();
        var register = await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = $"Seller Plano {Guid.NewGuid():N}",
            Email = email,
            Password = password,
            PhoneNumber = "11987770000",
            PlanId = planId,
            BillingPeriod = SubscriptionBillingPeriod.Monthly
        });

        register.StatusCode.Should().Be(
            HttpStatusCode.Created,
            await register.Content.ReadAsStringAsync());
        await _factory.ConfirmEmailAsync(email);

        var login = await client.PostAsJsonAsync("/api/auth/login/seller", new LoginRequestDto
        {
            Email = email,
            Password = password
        });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = await login.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);

        Guid userId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            userId = (await db.Users.SingleAsync(x => x.Email == email)).Id;
            var selection = await db.SellerPlanSelections.SingleAsync(x => x.UserId == userId);
            selection.PlanId.Should().Be(planId);
            selection.BillingPeriod.Should().Be(SubscriptionBillingPeriod.Monthly);
        }

        var storeResponse = await client.PostAsJsonAsync("/api/stores", new CreateStoreRequestDto
        {
            Name = "Loja Plano",
            Slug = $"loja-plano-{Guid.NewGuid():N}",
            PhoneNumber = "11987770000",
            CuisineType = "Pizza",
            MaxDeliveryRadiusKm = 5
        });
        storeResponse.StatusCode.Should().Be(HttpStatusCode.Created, await storeResponse.Content.ReadAsStringAsync());
        var store = await storeResponse.Content.ReadFromJsonAsync<StoreResponseDto>();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var subscription = await db.SellerSubscriptions.SingleAsync(x => x.StoreId == store!.Id);
            subscription.PlanId.Should().Be(planId);
            subscription.BillingPeriod.Should().Be(SubscriptionBillingPeriod.Monthly);
            subscription.PlanDescriptionSnapshot.Should().NotBeNullOrWhiteSpace();
            subscription.TrialDaysSnapshot.Should().Be(15);
            subscription.StartDateUtc.Kind.Should().Be(DateTimeKind.Utc);
            subscription.TrialEndsAtUtc.Should().BeCloseTo(subscription.StartDateUtc.AddDays(15), TimeSpan.FromMinutes(1));
            subscription.NextBillingDateUtc.Should().Be(subscription.TrialEndsAtUtc);

            var charges = await db.SellerSubscriptionCharges.Where(x => x.StoreId == store!.Id).ToListAsync();
            charges.Should().ContainSingle();
            charges[0].Status.Should().Be(SellerSubscriptionChargeStatus.AwaitingGateway);
            charges[0].Amount.Should().Be(subscription.PlanAmount);
            charges[0].DueDateUtc.Should().Be(subscription.NextBillingDateUtc);
            charges[0].IdempotencyKey.Should().Be($"store:{store!.Id}:initial");

            (await db.SellerPlanSelections.AnyAsync(x => x.UserId == userId)).Should().BeFalse();
        }

        var my = await client.GetFromJsonAsync<SellerSubscriptionMyResponseDto>("/api/subscriptions/my");
        my.Should().NotBeNull();
        my!.HasPendingGatewayCharge.Should().BeTrue();
        my.BillingPeriod.Should().Be(SubscriptionBillingPeriod.Monthly);
        my.PlanLevel.Should().NotBeNull();
        my.PendingChargeStatus.Should().Be("Boleto aguardando integração");
    }

    [Fact]
    public async Task Upgrade_ShouldBeAllowedOnlyToSuperiorLevel()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (email, password) = ($"plan.upgrade.{Guid.NewGuid():N}@urbeat.local", "SenhaForte123");
        _ = await _factory.RegisterSellerAsync(client, email, password, "Seller Upgrade");

        var login = await client.PostAsJsonAsync("/api/auth/login/seller", new LoginRequestDto
        {
            Email = email,
            Password = password
        });
        var token = await login.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);

        var storeResponse = await client.PostAsJsonAsync("/api/stores", new CreateStoreRequestDto
        {
            Name = "Loja Upgrade",
            Slug = $"loja-upgrade-{Guid.NewGuid():N}",
            PhoneNumber = "11987770000",
            CuisineType = "Pizza",
            MaxDeliveryRadiusKm = 5
        });
        storeResponse.StatusCode.Should().Be(HttpStatusCode.Created, await storeResponse.Content.ReadAsStringAsync());
        var store = await storeResponse.Content.ReadFromJsonAsync<StoreResponseDto>();

        Guid lowerPlanId;
        Guid higherPlanId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var current = await db.SellerSubscriptions.SingleAsync(x => x.StoreId == store!.Id);
            var currentPlan = await db.Plans.SingleAsync(x => x.Id == current.PlanId);
            var lower = new Plan
            {
                Name = $"Inferior {Guid.NewGuid():N}",
                Amount = 9.90m,
                IsActive = true,
                Level = Math.Max(0, currentPlan.Level - 1),
                MonthlyEnabled = true
            };
            var higher = new Plan
            {
                Name = $"Superior {Guid.NewGuid():N}",
                Amount = 99.90m,
                IsActive = true,
                Level = currentPlan.Level + 2,
                MonthlyEnabled = true,
                AnnualEnabled = true,
                AnnualDiscountPercent = 25
            };
            db.Plans.AddRange(lower, higher);
            await db.SaveChangesAsync();
            lowerPlanId = lower.Id;
            higherPlanId = higher.Id;
        }

        var downgrade = await client.PostAsJsonAsync("/api/subscriptions/contract", new ContractSellerSubscriptionRequestDto
        {
            StoreId = store!.Id,
            PlanId = lowerPlanId,
            BillingPeriod = SubscriptionBillingPeriod.Monthly
        });
        downgrade.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var upgrade = await client.PostAsJsonAsync("/api/subscriptions/contract", new ContractSellerSubscriptionRequestDto
        {
            StoreId = store.Id,
            PlanId = higherPlanId,
            BillingPeriod = SubscriptionBillingPeriod.Annual
        });
        upgrade.StatusCode.Should().Be(HttpStatusCode.Created, await upgrade.Content.ReadAsStringAsync());
        var contract = await upgrade.Content.ReadFromJsonAsync<ContractSellerSubscriptionResponseDto>();
        contract!.BillingPeriod.Should().Be(SubscriptionBillingPeriod.Annual);
        contract.AwaitingGateway.Should().BeTrue();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var subscription = await db.SellerSubscriptions.SingleAsync(x => x.StoreId == store.Id);
            subscription.PlanId.Should().Be(higherPlanId);
            subscription.BillingPeriod.Should().Be(SubscriptionBillingPeriod.Annual);
            (await db.SellerSubscriptionChanges.CountAsync(x => x.SubscriptionId == subscription.Id)).Should().Be(1);
            (await db.SellerSubscriptionCharges.CountAsync(x => x.StoreId == store.Id)).Should().Be(2);
        }
    }

    [Fact]
    public async Task GatewayConfiguration_ShouldBeIndependentPerPlanAndHiddenFromPublicAndSeller()
    {
        var adminClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var adminLogin = await adminClient.PostAsJsonAsync("/api/auth/login/admin", new LoginRequestDto
        {
            Email = "admin@urbeat.local",
            Password = "Admin12345"
        });
        var adminToken = await adminLogin.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken!.AccessToken);

        var firstPlan = await CreatePlanAsync(adminClient, "Gateway A");
        var secondPlan = await CreatePlanAsync(adminClient, "Gateway B");

        var firstSave = await adminClient.PutAsJsonAsync($"/api/admin/plans/{firstPlan.Id}/gateway-configuration", new UpdateGatewayConfigurationRequestDto
        {
            Provider = "Asaas",
            Environment = "Sandbox",
            ApiBaseUrl = "https://sandbox.asaas.com",
            WebhookUrl = "https://urbeat.test/webhooks/plan-a",
            ApiKeySecretReference = "vault://urbeat/subscription/plan-a/api-key",
            WebhookSecretReference = "vault://urbeat/subscription/plan-a/webhook",
            IsEnabled = true
        });
        var secondSave = await adminClient.PutAsJsonAsync($"/api/admin/plans/{secondPlan.Id}/gateway-configuration", new UpdateGatewayConfigurationRequestDto
        {
            Provider = "Asaas",
            Environment = "Production",
            ApiBaseUrl = "https://api.asaas.com",
            WebhookUrl = "https://urbeat.test/webhooks/plan-b",
            ApiKeySecretReference = "vault://urbeat/subscription/plan-b/api-key",
            WebhookSecretReference = "vault://urbeat/subscription/plan-b/webhook",
            IsEnabled = false
        });

        firstSave.StatusCode.Should().Be(HttpStatusCode.OK, await firstSave.Content.ReadAsStringAsync());
        secondSave.StatusCode.Should().Be(HttpStatusCode.OK, await secondSave.Content.ReadAsStringAsync());

        var firstRead = await adminClient.GetFromJsonAsync<GatewayConfigurationResponseDto>(
            $"/api/admin/plans/{firstPlan.Id}/gateway-configuration");
        var secondRead = await adminClient.GetFromJsonAsync<GatewayConfigurationResponseDto>(
            $"/api/admin/plans/{secondPlan.Id}/gateway-configuration");

        firstRead!.PlanId.Should().Be(firstPlan.Id);
        secondRead!.PlanId.Should().Be(secondPlan.Id);
        firstRead.Environment.Should().Be("Sandbox");
        secondRead.Environment.Should().Be("Production");
        firstRead.ApiBaseUrl.Should().Be("https://sandbox.asaas.com");
        secondRead.ApiBaseUrl.Should().Be("https://api.asaas.com");
        firstRead.HasApiKey.Should().BeTrue();
        secondRead.HasApiKey.Should().BeTrue();
        firstRead.HasWebhookSecret.Should().BeTrue();
        secondRead.HasWebhookSecret.Should().BeTrue();

        var firstRaw = await adminClient.GetStringAsync($"/api/admin/plans/{firstPlan.Id}/gateway-configuration");
        firstRaw.Should().NotContain("secret-key-plan-a");
        firstRaw.Should().NotContain("secret-hook-plan-a");
        firstRaw.Should().NotContain("secret-key-plan-b");
        firstRaw.Should().NotContain("vault://urbeat/subscription/plan-a/api-key");
        firstRaw.Should().NotContain("vault://urbeat/subscription/plan-a/webhook");

        var legacyGlobal = await adminClient.GetAsync("/api/admin/plans/gateway-configuration");
        legacyGlobal.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);

        var publicClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var publicRaw = await publicClient.GetStringAsync("/api/public/plans");
        publicRaw.Should().NotContain("ApiKey");
        publicRaw.Should().NotContain("Webhook");
        publicRaw.Should().NotContain("secret");
        publicRaw.Should().NotContain("sandbox.asaas.com");

        var sellerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var sellerEmail = $"gateway.seller.{Guid.NewGuid():N}@urbeat.local";
        const string sellerPassword = "SenhaForte123";
        await _factory.RegisterSellerAsync(sellerClient, sellerEmail, sellerPassword, "Gateway Seller");
        var sellerLogin = await sellerClient.PostAsJsonAsync("/api/auth/login/seller", new LoginRequestDto
        {
            Email = sellerEmail,
            Password = sellerPassword
        });
        var sellerToken = await sellerLogin.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        sellerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sellerToken!.AccessToken);
        var sellerRaw = await sellerClient.GetStringAsync("/api/subscriptions/plans");
        sellerRaw.Should().NotContain("ApiKey");
        sellerRaw.Should().NotContain("Webhook");
        sellerRaw.Should().NotContain("secret");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var configs = await db.SubscriptionGatewayConfigurations
            .Where(x => x.PlanId == firstPlan.Id || x.PlanId == secondPlan.Id)
            .ToListAsync();
        configs.Should().HaveCount(2);
        configs.Single(x => x.PlanId == firstPlan.Id).ApiKeySecretReference.Should().Be("vault://urbeat/subscription/plan-a/api-key");
        configs.Single(x => x.PlanId == secondPlan.Id).ApiKeySecretReference.Should().Be("vault://urbeat/subscription/plan-b/api-key");
    }

    private static async Task<PlanResponseDto> CreatePlanAsync(HttpClient adminClient, string name)
    {
        var response = await adminClient.PostAsJsonAsync("/api/admin/plans", new CreatePlanRequestDto
        {
            Name = $"{name} {Guid.NewGuid():N}",
            MonthlyAmount = 19.90m,
            Description = name,
            IsActive = true,
            Level = 10
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var plan = await response.Content.ReadFromJsonAsync<PlanResponseDto>();
        plan.Should().NotBeNull();
        return plan!;
    }

    [Fact]
    public async Task StoreCreation_ShouldRejectInactiveSelectedPlanAtomically()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"plan.inactive.store.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        var adminClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var adminLogin = await adminClient.PostAsJsonAsync("/api/auth/login/admin", new LoginRequestDto
        {
            Email = "admin@urbeat.local",
            Password = "Admin12345"
        });
        var adminToken = await adminLogin.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken!.AccessToken);
        var plan = await CreatePlanAsync(adminClient, "Plano que será inativado");
        var planId = plan.Id;

        var register = await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = $"Seller Plano Inativo {Guid.NewGuid():N}",
            Email = email,
            Password = password,
            PhoneNumber = "11987770000",
            PlanId = planId,
            BillingPeriod = SubscriptionBillingPeriod.Monthly
        });
        register.StatusCode.Should().Be(HttpStatusCode.Created, await register.Content.ReadAsStringAsync());
        await _factory.ConfirmEmailAsync(email);

        var login = await client.PostAsJsonAsync("/api/auth/login/seller", new LoginRequestDto
        {
            Email = email,
            Password = password
        });
        var token = await login.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);

        var deactivate = await adminClient.PatchAsJsonAsync($"/api/admin/plans/{planId}/status", new UpdatePlanStatusRequestDto
        {
            IsActive = false
        });
        deactivate.StatusCode.Should().Be(HttpStatusCode.OK);

        var storeResponse = await client.PostAsJsonAsync("/api/stores", new CreateStoreRequestDto
        {
            Name = "Loja Plano Inativo",
            Slug = $"loja-plano-inativo-{Guid.NewGuid():N}",
            PhoneNumber = "11987770000",
            CuisineType = "Pizza",
            MaxDeliveryRadiusKm = 5
        });

        storeResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await storeResponse.Content.ReadAsStringAsync()).Should().Contain("invalid_plan_selection");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.SingleAsync(x => x.Email == email);
        (await db.Stores.AnyAsync(x => x.OwnerUserId == user.Id)).Should().BeFalse();
        (await db.SellerSubscriptions.AnyAsync(x => x.SellerUserId == user.Id)).Should().BeFalse();
        (await db.SellerSubscriptionCharges.AnyAsync(x => x.SellerUserId == user.Id)).Should().BeFalse();
        (await db.SellerPlanSelections.AnyAsync(x => x.UserId == user.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task StoreCreation_ShouldConsumeSelectionOnceAndNotDuplicateSubscriptionOrStub()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"plan.idempotent.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";
        var planId = await _factory.GetDefaultPlanIdAsync();

        var register = await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = $"Seller Idempotente {Guid.NewGuid():N}",
            Email = email,
            Password = password,
            PhoneNumber = "11987770000",
            PlanId = planId,
            BillingPeriod = SubscriptionBillingPeriod.Monthly
        });
        register.StatusCode.Should().Be(HttpStatusCode.Created, await register.Content.ReadAsStringAsync());
        await _factory.ConfirmEmailAsync(email);

        var login = await client.PostAsJsonAsync("/api/auth/login/seller", new LoginRequestDto
        {
            Email = email,
            Password = password
        });
        var token = await login.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);

        var request = new CreateStoreRequestDto
        {
            Name = "Loja Idempotente",
            Slug = $"loja-idempotente-{Guid.NewGuid():N}",
            PhoneNumber = "11987770000",
            CuisineType = "Pizza",
            MaxDeliveryRadiusKm = 5
        };

        var first = await client.PostAsJsonAsync("/api/stores", request);
        first.StatusCode.Should().Be(HttpStatusCode.Created, await first.Content.ReadAsStringAsync());
        var second = await client.PostAsJsonAsync("/api/stores", request);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.SingleAsync(x => x.Email == email);
        (await db.Stores.CountAsync(x => x.OwnerUserId == user.Id)).Should().Be(1);
        (await db.SellerSubscriptions.CountAsync(x => x.SellerUserId == user.Id)).Should().Be(1);
        (await db.SellerSubscriptionCharges.CountAsync(x => x.SellerUserId == user.Id)).Should().Be(1);
        (await db.SellerPlanSelections.AnyAsync(x => x.UserId == user.Id)).Should().BeFalse();
    }

    private async Task AssertNoUserAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.Users.AnyAsync(x => x.Email == email)).Should().BeFalse();
    }
}
