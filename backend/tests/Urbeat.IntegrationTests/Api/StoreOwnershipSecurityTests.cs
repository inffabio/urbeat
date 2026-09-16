using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.IntegrationTests.Infrastructure;
using Microsoft.IdentityModel.Tokens;

namespace Urbeat.IntegrationTests.Api;

public sealed class StoreOwnershipSecurityTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Issuer = "urbeat";
    private const string Audience = "urbeat-api";
    private const string Secret = "CHANGE_ME_MINIMUM_32_CHARS_SECRET";

    private readonly TestWebApplicationFactory _factory;

    public StoreOwnershipSecurityTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateDeliveryTime_ShouldReturnForbidden_WhenStoreBelongsToAnotherSeller()
    {
        var sellerAClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (_, storeA, _) = await CreateStoreAsync(sellerAClient);

        var sellerBClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (tokenB, _, _) = await CreateStoreAsync(sellerBClient);
        sellerBClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);

        var response = await sellerBClient.PostAsJsonAsync("/api/stores/delivery-times", new CreateDeliveryTimeRequestDto
        {
            StoreId = storeA,
            MinTimeMinutes = 20,
            MaxTimeMinutes = 40
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateDeliveryTime_ShouldCreate_WhenSellerOwnsStore()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (token, storeId, _) = await CreateStoreAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/stores/delivery-times", new CreateDeliveryTimeRequestDto
        {
            StoreId = storeId,
            MinTimeMinutes = 20,
            MaxTimeMinutes = 40
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateDeliveryNeighborhood_ShouldCreate_WhenSellerOwnsStoreCity()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (token, _, city) = await CreateStoreAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/stores/delivery-neighborhoods", new CreateDeliveryNeighborhoodRequestDto
        {
            Neighborhood = $"Centro {Guid.NewGuid():N}",
            City = city
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<DeliveryNeighborhoodResponseDto>();
        created.Should().NotBeNull();
        created!.City.Should().Be(city);
    }

    [Fact]
    public async Task CreateDeliveryNeighborhood_ShouldReturnForbidden_WhenSellerTargetsAnotherCity()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (token, _, _) = await CreateStoreAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/stores/delivery-neighborhoods", new CreateDeliveryNeighborhoodRequestDto
        {
            Neighborhood = $"Copacabana {Guid.NewGuid():N}",
            City = $"Rio de Janeiro {Guid.NewGuid():N}"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateDeliveryNeighborhood_ShouldReturnBadRequest_WhenStoreHasNoAddress()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (token, _, _) = await CreateStoreAsync(client, withAddress: false);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/stores/delivery-neighborhoods", new CreateDeliveryNeighborhoodRequestDto
        {
            Neighborhood = $"Centro {Guid.NewGuid():N}",
            City = "Sao Paulo"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateDeliveryNeighborhood_ShouldCreate_ForAdmin()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", BuildAdminToken());

        var response = await client.PostAsJsonAsync("/api/admin/delivery-neighborhoods", new CreateDeliveryNeighborhoodRequestDto
        {
            Neighborhood = $"Bairro {Guid.NewGuid():N}",
            City = "Sao Paulo"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private async Task<(string AccessToken, Guid StoreId, string City)> CreateStoreAsync(HttpClient client, bool withAddress = true)
    {
        var email = $"ownership.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        await _factory.RegisterSellerAsync(client, email, password, "Seller Ownership", "11984443333");

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login/seller", new LoginRequestDto
        {
            Email = email,
            Password = password
        });

        var token = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);

        var createStoreResponse = await client.PostAsJsonAsync("/api/stores", new CreateStoreRequestDto
        {
            Name = "Loja Ownership",
            Slug = $"loja-ownership-{Guid.NewGuid():N}",
            PhoneNumber = "11982221111",
            CuisineType = "Pizza",
            MaxDeliveryRadiusKm = 5,
        });

        var store = await createStoreResponse.Content.ReadFromJsonAsync<StoreResponseDto>();
        var city = string.Empty;

        if (withAddress)
        {
            city = $"Sao Paulo {Guid.NewGuid():N}";

            var addressResponse = await client.PutAsJsonAsync($"/api/stores/{store!.Id}/address", new
            {
                street = "Rua Ownership",
                number = "100",
                neighborhood = "Centro",
                city,
                state = "SP",
                zipCode = "01001000",
                latitude = -23.5505,
                longitude = -46.6333
            });
            addressResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        return (token.AccessToken, store!.Id, city);
    }

    private static string BuildAdminToken()
    {
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Email, "admin@urbeat.local"),
            new(ClaimTypes.Role, "Admin")
        };

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
