using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.IntegrationTests.Infrastructure;

namespace Urbeat.IntegrationTests.Api;

public sealed class PlanManagementFlowTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public PlanManagementFlowTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AdminCrudPlans_ShouldExposeOnlyActivePlansToSeller()
    {
        var adminClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var adminToken = await LoginAdminAsync(adminClient);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var createBasicResponse = await adminClient.PostAsJsonAsync("/api/admin/plans", new CreatePlanRequestDto
        {
            Name = "Basico",
            Amount = 39.90m,
            Description = "Plano basico",
            IsActive = true
        });

        var createProResponse = await adminClient.PostAsJsonAsync("/api/admin/plans", new CreatePlanRequestDto
        {
            Name = "Pro",
            Amount = 79.90m,
            Description = "Plano pro",
            IsActive = true
        });

        createBasicResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        createProResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var basicPlan = await createBasicResponse.Content.ReadFromJsonAsync<PlanResponseDto>();
        var proPlan = await createProResponse.Content.ReadFromJsonAsync<PlanResponseDto>();
        basicPlan.Should().NotBeNull();
        proPlan.Should().NotBeNull();

        var updateResponse = await adminClient.PutAsJsonAsync($"/api/admin/plans/{basicPlan!.Id}", new UpdatePlanRequestDto
        {
            Name = "Basico Plus",
            Amount = 49.90m,
            Description = "Plano basico atualizado"
        });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var deactivateResponse = await adminClient.PatchAsJsonAsync($"/api/admin/plans/{proPlan!.Id}/status", new UpdatePlanStatusRequestDto
        {
            IsActive = false
        });
        deactivateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var adminList = await adminClient.GetFromJsonAsync<List<PlanResponseDto>>("/api/admin/plans");
        adminList.Should().NotBeNull();
        adminList!.Should().Contain(x => x.Id == basicPlan.Id && x.Name == "Basico Plus");
        adminList.Should().Contain(x => x.Id == proPlan.Id && !x.IsActive);

        var sellerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var sellerToken = await RegisterAndLoginSellerAsync(sellerClient);
        sellerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sellerToken);

        var sellerPlans = await sellerClient.GetFromJsonAsync<List<PlanResponseDto>>("/api/subscriptions/plans");
        sellerPlans.Should().NotBeNull();
        sellerPlans!.Should().ContainSingle(x => x.Id == basicPlan.Id && x.Name == "Basico Plus" && x.IsActive);
        sellerPlans.Should().NotContain(x => x.Id == proPlan.Id);
    }

    [Fact]
    public async Task PublicOffer_ShouldExposeActivePlansWithTrialAndBenefitsOnly()
    {
        var adminClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var adminToken = await LoginAdminAsync(adminClient);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var suffix = Guid.NewGuid().ToString("N");
        var activeName = $"Oferta Ativa {suffix}";
        var inactiveName = $"Oferta Inativa {suffix}";

        var activeResponse = await adminClient.PostAsJsonAsync("/api/admin/plans", new CreatePlanRequestDto
        {
            Name = activeName,
            Amount = 27.90m,
            Description = "Plano público",
            IsActive = true,
            TrialDays = 9,
            Benefits = ["Cardápio digital", "Relatórios"]
        });
        var inactiveResponse = await adminClient.PostAsJsonAsync("/api/admin/plans", new CreatePlanRequestDto
        {
            Name = inactiveName,
            Amount = 17.90m,
            Description = "Plano oculto",
            IsActive = false,
            TrialDays = 3
        });

        activeResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        inactiveResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var publicClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var rawOffer = await publicClient.GetStringAsync("/api/public/plans");

        using var document = JsonDocument.Parse(rawOffer);
        var plans = document.RootElement.GetProperty("plans").EnumerateArray().ToList();
        var activePlan = plans.Single(x => x.GetProperty("name").GetString() == activeName);

        activePlan.GetProperty("trialDays").GetInt32().Should().Be(9);
        activePlan.GetProperty("benefits").EnumerateArray().Select(x => x.GetString())
            .Should().Equal("Cardápio digital", "Relatórios");
        plans.Should().NotContain(x => x.GetProperty("name").GetString() == inactiveName);

        foreach (var plan in plans)
        {
            plan.TryGetProperty("isActive", out _).Should().BeFalse();
            plan.TryGetProperty("createdAtUtc", out _).Should().BeFalse();
            plan.TryGetProperty("updatedAtUtc", out _).Should().BeFalse();
        }
    }

    [Fact]
    public async Task PlanWrites_ShouldRejectNonAdminUsers()
    {
        var anonymousClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var anonymousResponse = await anonymousClient.PostAsJsonAsync("/api/admin/plans", new CreatePlanRequestDto
        {
            Name = "Plano Anonimo",
            Amount = 10m
        });
        anonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var sellerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var sellerToken = await RegisterAndLoginSellerAsync(sellerClient);
        sellerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sellerToken);

        var sellerResponse = await sellerClient.PostAsJsonAsync("/api/admin/plans", new CreatePlanRequestDto
        {
            Name = "Plano Vendedor",
            Amount = 10m
        });
        sellerResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreatePlan_ShouldRejectTrialDaysOutsideAllowedRange()
    {
        var adminClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var adminToken = await LoginAdminAsync(adminClient);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var response = await adminClient.PostAsJsonAsync("/api/admin/plans", new CreatePlanRequestDto
        {
            Name = $"Trial Invalido {Guid.NewGuid():N}",
            Amount = 19.90m,
            TrialDays = 366
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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

    private async Task<string> RegisterAndLoginSellerAsync(HttpClient client)
    {
        var email = $"plan.seller.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = "Plan Seller",
            Email = email,
            Password = password,
            PhoneNumber = "11987776666"
        });
        await _factory.ConfirmEmailAsync(email);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login/seller", new LoginRequestDto
        {
            Email = email,
            Password = password
        });

        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        token.Should().NotBeNull();
        return token!.AccessToken;
    }
}
