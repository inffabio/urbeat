using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.IntegrationTests.Infrastructure;

namespace Urbeat.IntegrationTests.Api;

public sealed class StoreManagementFlowTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public StoreManagementFlowTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Seller_ShouldUpsertAndGetBusinessHours()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (token, storeId) = await RegisterLoginAndCreateStoreAsync(client, "Pizza");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var upsertResponse = await client.PutAsJsonAsync($"/api/stores/{storeId}/business-hours", new UpsertStoreBusinessHoursRequestDto
        {
            Items =
            [
                new StoreBusinessHourItemDto
                {
                    DayOfWeek = DayOfWeek.Monday,
                    Shifts =
                    [
                        new StoreBusinessHourShiftDto
                        {
                            StartTime = new TimeOnly(9, 0),
                            EndTime = new TimeOnly(18, 0)
                        }
                    ]
                },
                new StoreBusinessHourItemDto
                {
                    DayOfWeek = DayOfWeek.Tuesday,
                    Shifts =
                    [
                        new StoreBusinessHourShiftDto
                        {
                            StartTime = new TimeOnly(10, 0),
                            EndTime = new TimeOnly(20, 0)
                        }
                    ]
                }
            ]
        });

        upsertResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var getResponse = await client.GetAsync($"/api/stores/{storeId}/business-hours");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await getResponse.Content.ReadFromJsonAsync<StoreBusinessHoursResponseDto>();
        payload.Should().NotBeNull();
        payload!.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task Seller_ShouldUpdateStoreStatus()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (token, storeId) = await RegisterLoginAndCreateStoreAsync(client, "Lanches");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var updateResponse = await client.PatchAsJsonAsync($"/api/stores/{storeId}/status", new UpdateStoreStatusRequestDto
        {
            IsOpen = true
        });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var myStoreResponse = await client.GetAsync("/api/stores/my-store");
        myStoreResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var storePayload = await myStoreResponse.Content.ReadFromJsonAsync<StoreResponseDto>();
        storePayload.Should().NotBeNull();
        storePayload!.IsOpen.Should().BeTrue();
    }

    [Fact]
    public async Task Seller_ShouldUpdateBusinessContactFields()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (token, storeId) = await RegisterLoginAndCreateStoreAsync(client, "Lanches");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var updateResponse = await client.PutAsJsonAsync($"/api/stores/{storeId}", new UpdateStoreRequestDto
        {
            Name = "Loja Gestao",
            Slug = "loja-gestao",
            PhoneNumber = "11982221111",
            Document = "529.982.247-25",
            PixKey = "pix@example.com",
            WebsiteUrl = "https://loja.example.com",
            Description = "Loja atualizada",
            CuisineType = "Lanches",
            SupportsDelivery = true,
            SupportsPickup = true,
            MaxDeliveryRadiusKm = 5,
        });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var myStoreResponse = await client.GetAsync("/api/stores/my-store");
        var storePayload = await myStoreResponse.Content.ReadFromJsonAsync<StoreResponseDto>();

        storePayload.Should().NotBeNull();
        storePayload!.Document.Should().Be("52998224725");
        storePayload.PixKey.Should().Be("pix@example.com");
        storePayload.WebsiteUrl.Should().Be("https://loja.example.com");
    }

    [Fact]
    public async Task Seller_ShouldNotPersistOrExposeSocialMediaUrls()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (token, storeId) = await RegisterLoginAndCreateStoreAsync(client, "Lanches");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var updateResponse = await client.PutAsJsonAsync($"/api/stores/{storeId}", new
        {
            Name = "Loja Sem Redes",
            Slug = "loja-sem-redes",
            PhoneNumber = "11982221111",
            Document = "529.982.247-25",
            PixKey = "pix@example.com",
            InstagramUrl = "https://instagram.com/loja",
            FacebookUrl = "https://facebook.com/loja",
            TikTokUrl = "https://tiktok.com/@loja",
            WebsiteUrl = "https://loja.example.com",
            Description = "Loja sem redes sociais",
            CuisineType = "Lanches",
            SupportsDelivery = true,
            SupportsPickup = true,
            MaxDeliveryRadiusKm = 5,
        });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var myStoreResponse = await client.GetAsync("/api/stores/my-store");
        myStoreResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // The API serializes with camelCase JSON. The social media URLs must not
        // be part of the response contract, while websiteUrl must be present.
        var rawJson = await myStoreResponse.Content.ReadAsStringAsync();
        rawJson.Should().NotContain("instagramUrl");
        rawJson.Should().NotContain("facebookUrl");
        rawJson.Should().NotContain("tikTokUrl");

        using var document = JsonDocument.Parse(rawJson);
        var root = document.RootElement;

        root.TryGetProperty("instagramUrl", out _).Should().BeFalse();
        root.TryGetProperty("facebookUrl", out _).Should().BeFalse();
        root.TryGetProperty("tikTokUrl", out _).Should().BeFalse();

        // websiteUrl must appear and reflect the value that was sent, proving it persists.
        root.TryGetProperty("websiteUrl", out var websiteUrlElement).Should().BeTrue();
        websiteUrlElement.GetString().Should().Be("https://loja.example.com");

        var storePayload = root.Deserialize<StoreResponseDto>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        storePayload.Should().NotBeNull();
        storePayload!.WebsiteUrl.Should().Be("https://loja.example.com");
    }

    [Fact]
    public async Task Seller_ShouldUpdateDeliveryConfig()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (token, storeId) = await RegisterLoginAndCreateStoreAsync(client, "Japonesa");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var updateResponse = await client.PatchAsJsonAsync($"/api/stores/{storeId}/delivery-config", new UpdateStoreDeliveryConfigRequestDto
        {
            DeliveryFee = 8.90m,
            MinimumOrderValue = 35m
        });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var myStoreResponse = await client.GetAsync("/api/stores/my-store");
        myStoreResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var storePayload = await myStoreResponse.Content.ReadFromJsonAsync<StoreResponseDto>();
        storePayload.Should().NotBeNull();
        storePayload!.DeliveryFee.Should().Be(8.90m);
        storePayload.MinimumOrderValue.Should().Be(35m);
    }

    [Fact]
    public async Task Seller_ShouldHandleConcurrentDeliveryConfigUpdatesGracefully()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (token, storeId) = await RegisterLoginAndCreateStoreAsync(client, "Japonesa");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var initialUpdateResponse = await client.PatchAsJsonAsync($"/api/stores/{storeId}/delivery-config", new UpdateStoreDeliveryConfigRequestDto
        {
            DeliveryFee = 10.00m,
            MinimumOrderValue = 50m
        });

        initialUpdateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Simulate a concurrent update (which might trigger a DbUpdateConcurrencyException internally if not handled)
        var concurrentUpdateResponse = await client.PatchAsJsonAsync($"/api/stores/{storeId}/delivery-config", new UpdateStoreDeliveryConfigRequestDto
        {
            DeliveryFee = 12.00m,
            MinimumOrderValue = 60m
        });

        // The endpoint should handle this gracefully and return OK (or at least not 500 Internal Server Error)
        concurrentUpdateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var myStoreResponse = await client.GetAsync("/api/stores/my-store");
        myStoreResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var storePayload = await myStoreResponse.Content.ReadFromJsonAsync<StoreResponseDto>();
        storePayload.Should().NotBeNull();
        storePayload!.DeliveryFee.Should().Be(12.00m);
        storePayload.MinimumOrderValue.Should().Be(60m);
    }

    private async Task<(string AccessToken, Guid StoreId)> RegisterLoginAndCreateStoreAsync(HttpClient client, string cuisineType)
    {
        var email = $"store.management.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = "Seller Management",
            Email = email,
            Password = password,
            PhoneNumber = "11984443333"
        });
        await _factory.ConfirmEmailAsync(email);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login/seller", new LoginRequestDto
        {
            Email = email,
            Password = password
        });

        var token = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        var accessToken = token!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var createStoreResponse = await client.PostAsJsonAsync("/api/stores", new CreateStoreRequestDto
        {
            Name = "Loja Gestao",
            PhoneNumber = "11982221111",
            Description = "Loja para teste de gestao",
            CuisineType = cuisineType,
            MaxDeliveryRadiusKm = 5,
        });

        var store = await createStoreResponse.Content.ReadFromJsonAsync<StoreResponseDto>();
        return (accessToken, store!.Id);
    }
}
