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

public sealed class NeonighborhoodsDeliveryFlowTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public NeonighborhoodsDeliveryFlowTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetNeighborhoodsByStore_ShouldReturnOnlyStoreCityNeighborhoods_WhenRadiusIsSet()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (accessToken, storeId, storeCity) = await RegisterLoginCreateStoreWithAddress(client);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        db.DeliveryNeighborhoods.Add(new DeliveryNeighborhood
        {
            Neighborhood = "Vila Madalena",
            NormalizedName = "vila madalena",
            City = storeCity,
            CityId = Guid.NewGuid(),
            Latitude = -23.5500,
            Longitude = -46.6900,
            IsActive = true,
            Source = "test"
        });
        db.DeliveryNeighborhoods.Add(new DeliveryNeighborhood
        {
            Neighborhood = "Ipanema",
            NormalizedName = "ipanema",
            City = "Rio de Janeiro",
            CityId = Guid.NewGuid(),
            Latitude = -22.9833,
            Longitude = -43.2167,
            IsActive = true,
            Source = "test"
        });
        await db.SaveChangesAsync();

        var response = await client.GetAsync($"/api/stores/delivery-neighborhoods-by-store?storeId={storeId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var neighborhoods = await response.Content.ReadFromJsonAsync<List<DeliveryNeighborhoodResponseDto>>();
        neighborhoods.Should().NotBeNull();
        neighborhoods!.Should().HaveCount(1);
        neighborhoods[0].Neighborhood.Should().Be("Vila Madalena");
    }

    [Fact]
    public async Task GetNeighborhoodsByStore_ShouldRequireAuth()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync($"/api/stores/delivery-neighborhoods-by-store?storeId={Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ImportByCity_ShouldReturnBadRequest_WhenCityIsEmpty()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (accessToken, _, _) = await RegisterLoginCreateStoreWithAddress(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.PostAsJsonAsync("/api/neighborhoods/import-by-city", new
        {
            city = "",
            uf = "SP"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ImportByCity_ShouldReturnBadRequest_WhenUfIsEmpty()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (accessToken, _, _) = await RegisterLoginCreateStoreWithAddress(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.PostAsJsonAsync("/api/neighborhoods/import-by-city", new
        {
            city = "Sao Paulo",
            uf = ""
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ImportByCity_ShouldRequireAuth()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.PostAsJsonAsync("/api/neighborhoods/import-by-city", new
        {
            city = "Sao Paulo",
            uf = "SP"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<(string AccessToken, Guid StoreId, string City)> RegisterLoginCreateStoreWithAddress(HttpClient client)
    {
        var email = $"delivery.test.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        var registerResponse = await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = "Seller Delivery Test",
            Email = email,
            Password = password,
            PhoneNumber = "11981112222"
        });
        registerResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        await _factory.ConfirmEmailAsync(email);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login/seller", new LoginRequestDto
        {
            Email = email,
            Password = password
        });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        token.Should().NotBeNull();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);

        var createStoreResponse = await client.PostAsJsonAsync("/api/stores", new CreateStoreRequestDto
        {
            Name = "Loja Delivery Test",
            PhoneNumber = "11983334444",
            Description = "Loja para teste de delivery",
            CuisineType = "Lanches",
            MaxDeliveryRadiusKm = 10
        });
        createStoreResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var store = await createStoreResponse.Content.ReadFromJsonAsync<StoreResponseDto>();
        store.Should().NotBeNull();

        var addressResponse = await client.PutAsJsonAsync($"/api/stores/{store!.Id}/address", new
        {
            street = "Rua Teste",
            number = "100",
            neighborhood = "Centro",
            city = "Sao Paulo",
            state = "SP",
            zipCode = "01001000",
            latitude = -23.5505,
            longitude = -46.6333
        });
        addressResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        return (token.AccessToken, store.Id, "Sao Paulo");
    }
}
