using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Urbeat.Application.DTOs;
using Urbeat.Application.DTOs.Admin;
using Urbeat.Infrastructure.Persistence;
using Urbeat.IntegrationTests.Infrastructure;

namespace Urbeat.IntegrationTests.Api;

/// <summary>
/// Contract of the administrative store directory: admin-only access, onboarding visibility,
/// independent filters, stable pagination and no operational/identity secrets in the payload.
/// </summary>
public sealed class AdminStoresFlowTests
{
    private const string AdminEmail = "admin@urbeat.local";
    private const string AdminPassword = "Admin12345";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Listing_ShouldRequireTheAdminRole()
    {
        using var factory = new TestWebApplicationFactory();
        var anonymous = factory.CreateClient();

        (await anonymous.GetAsync("/api/admin/stores")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

        var sellerEmail = $"admin.stores.seller.{Guid.NewGuid():N}@urbeat.local";
        await factory.RegisterSellerAsync(anonymous, sellerEmail, "Lojista123!", "Lojista Lojas");
        var sellerToken = await LoginAsync(anonymous, sellerEmail, "Lojista123!", "/api/auth/login/seller");

        var customerEmail = $"admin.stores.customer.{Guid.NewGuid():N}@urbeat.local";
        (await anonymous.PostAsJsonAsync("/api/auth/register/customer", new RegisterUserRequestDto
        {
            FullName = "Cliente Lojas",
            Email = customerEmail,
            Password = "Cliente123!",
            PhoneNumber = "11977777777"
        })).StatusCode.Should().Be(HttpStatusCode.Created);
        await factory.ConfirmEmailAsync(customerEmail);
        var customerToken = await LoginAsync(anonymous, customerEmail, "Cliente123!", "/api/auth/login/customer");

        foreach (var token in new[] { sellerToken!, customerToken! })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/stores");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            (await anonymous.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        var adminToken = await LoginAsync(anonymous, AdminEmail, AdminPassword, "/api/auth/login/admin");
        using var adminRequest = new HttpRequestMessage(HttpMethod.Get, "/api/admin/stores");
        adminRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        (await anonymous.SendAsync(adminRequest)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Listing_ShouldIncludeOnboardingStores_WithFiltersAndNoSecrets()
    {
        using var factory = new TestWebApplicationFactory();
        var client = factory.CreateClient();
        var adminToken = (await LoginAsync(client, AdminEmail, AdminPassword, "/api/auth/login/admin"))!;

        var sellerClient = factory.CreateClient();
        var sellerEmail = $"onboarding.{Guid.NewGuid():N}@urbeat.local";
        await factory.RegisterSellerAsync(sellerClient, sellerEmail, "Lojista123!", "Onboarding Store");
        var sellerToken = (await LoginAsync(sellerClient, sellerEmail, "Lojista123!", "/api/auth/login/seller"))!;

        var storeName = $"Wizard Store {Guid.NewGuid():N}";
        using var createStore = new HttpRequestMessage(HttpMethod.Post, "/api/stores");
        createStore.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sellerToken);
        createStore.Content = JsonContent.Create(new CreateStoreRequestDto
        {
            Name = storeName,
            Slug = $"wizard-store-{Guid.NewGuid():N}",
            PhoneNumber = "11982221111",
            CuisineType = "Pizza",
            MaxDeliveryRadiusKm = 5
        });

        var created = await sellerClient.SendAsync(createStore);
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/admin/stores?status=onboarding&search=Wizard&page=1&pageSize=20");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var listResponse = await client.SendAsync(listRequest);

        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        listResponse.Headers.CacheControl!.NoStore.Should().BeTrue();
        listResponse.Headers.Pragma.Should().Contain(pragma => pragma.Name == "no-cache");

        var body = await listResponse.Content.ReadAsStringAsync();
        var lower = body.ToLowerInvariant();
        lower.Should().NotContain("document");
        lower.Should().NotContain("pixkey");
        lower.Should().NotContain("phonenumber");

        var page = JsonSerializer.Deserialize<PagedAdminStoresDto>(body, JsonOptions)!;
        page.Page.Should().Be(1);
        page.PageSize.Should().Be(20);
        var item = page.Items.Single(summary => summary.Name == storeName);
        item.Status.Should().Be("Onboarding");
        item.IsPublished.Should().BeFalse();
        item.Subscription.Status.Should().NotBeNullOrWhiteSpace();
        item.Owner.Email.Should().Be(sellerEmail);
    }

    [Fact]
    public async Task Listing_ShouldPageDeterministically()
    {
        using var factory = new TestWebApplicationFactory();
        var adminClient = factory.CreateClient();
        var adminToken = (await LoginAsync(adminClient, AdminEmail, AdminPassword, "/api/auth/login/admin"))!;

        var names = new[] { "Alpha Store", "Bravo Store", "Charlie Store" };
        foreach (var name in names)
        {
            var sellerClient = factory.CreateClient();
            var email = $"paging.{Guid.NewGuid():N}@urbeat.local";
            await factory.RegisterSellerAsync(sellerClient, email, "Lojista123!", name);
            var token = (await LoginAsync(sellerClient, email, "Lojista123!", "/api/auth/login/seller"))!;

            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/stores");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = JsonContent.Create(new CreateStoreRequestDto
            {
                Name = name,
                Slug = $"{name.Split(' ')[0].ToLowerInvariant()}-{Guid.NewGuid():N}",
                PhoneNumber = "11982221111",
                CuisineType = "Pizza",
                MaxDeliveryRadiusKm = 5
            });
            (await sellerClient.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var firstPage = await GetPageAsync(adminClient, adminToken, "/api/admin/stores?page=1&pageSize=2");
        firstPage.TotalItems.Should().Be(3);
        firstPage.TotalPages.Should().Be(2);
        firstPage.Items.Should().HaveCount(2);

        var secondPage = await GetPageAsync(adminClient, adminToken, "/api/admin/stores?page=2&pageSize=2");
        secondPage.Items.Should().HaveCount(1);
        secondPage.Items.Select(item => item.Name)
            .Should().NotIntersectWith(firstPage.Items.Select(item => item.Name));
    }

    [Fact]
    public async Task Listing_ShouldHideK6Fixtures_WithoutDeletingThem()
    {
        using var factory = new TestWebApplicationFactory();
        var client = factory.CreateClient();
        var adminToken = (await LoginAsync(client, AdminEmail, AdminPassword, "/api/auth/login/admin"))!;

        var normalName = $"Padaria Real {Guid.NewGuid():N}";
        var normalSlug = $"padaria-{Guid.NewGuid():N}";
        var legitimateLoadName = $"Padaria Load {Guid.NewGuid():N}";
        var legitimateLoadSlug = $"load-padaria-{Guid.NewGuid():N}";
        var k6Name = "Store anchor";
        var k6Slug = "load-20261109204926-f5594855";

        var stores = new[]
        {
            (Name: normalName, Slug: normalSlug),
            // A legitimate store may use a load-* slug as long as it is outside the k6 shape.
            (Name: legitimateLoadName, Slug: legitimateLoadSlug),
            (Name: k6Name, Slug: k6Slug)
        };

        foreach (var (name, slug) in stores)
        {
            var sellerClient = factory.CreateClient();
            var email = $"load.hide.{Guid.NewGuid():N}@urbeat.local";
            await factory.RegisterSellerAsync(sellerClient, email, "Lojista123!", name);
            var token = (await LoginAsync(sellerClient, email, "Lojista123!", "/api/auth/login/seller"))!;

            using var createStore = new HttpRequestMessage(HttpMethod.Post, "/api/stores");
            createStore.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            createStore.Content = JsonContent.Create(new CreateStoreRequestDto
            {
                Name = name,
                Slug = slug,
                PhoneNumber = "11982221111",
                CuisineType = "Pizza",
                MaxDeliveryRadiusKm = 5
            });

            (await sellerClient.SendAsync(createStore)).StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var page = await GetPageAsync(client, adminToken, "/api/admin/stores?page=1&pageSize=20");

        page.TotalItems.Should().Be(2);
        page.TotalPages.Should().Be(1);
        page.Items.Select(item => item.Slug)
            .Should().BeEquivalentTo(normalSlug, legitimateLoadSlug)
            .And.NotContain(k6Slug);
        page.Items.Select(item => item.Name)
            .Should().BeEquivalentTo(normalName, legitimateLoadName)
            .And.NotContain(k6Name);

        // The k6 fixture is hidden from the listing but still persisted.
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await dbContext.Stores.CountAsync()).Should().Be(3);
        (await dbContext.Stores.CountAsync(store => store.Slug == k6Slug)).Should().Be(1);
    }

    private static async Task<PagedAdminStoresDto> GetPageAsync(
        HttpClient client,
        string token,
        string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<PagedAdminStoresDto>(JsonOptions))!;
    }

    private static async Task<string?> LoginAsync(
        HttpClient client,
        string email,
        string password,
        string path)
    {
        var response = await client.PostAsJsonAsync(path, new LoginRequestDto
        {
            Email = email,
            Password = password
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "login must succeed for " + email);
        var payload = await response.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        return payload?.AccessToken;
    }
}
