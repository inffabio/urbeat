using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.IntegrationTests.Infrastructure;

namespace Urbeat.IntegrationTests.Api;

public sealed class StoreFlowTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public StoreFlowTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Seller_ShouldCreateStore_AndGetMyStore()
    {
        var client = _factory.CreateClient(new()
        {
            AllowAutoRedirect = false
        });

        var email = $"store.seller.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        var registerResponse = await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = "Seller Store",
            Email = email,
            Password = password,
            PhoneNumber = "11988887777"
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

        var cuisineTypesResponse = await client.GetAsync("/api/stores/cuisine-types");
        cuisineTypesResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var cuisineTypes = await cuisineTypesResponse.Content.ReadFromJsonAsync<List<CuisineTypeResponseDto>>();
        cuisineTypes.Should().NotBeNullOrEmpty();
        cuisineTypes!.Any(x => x.Name == "Pizza").Should().BeTrue();

        var createStoreResponse = await client.PostAsJsonAsync("/api/stores", new CreateStoreRequestDto
        {
            Name = "Loja Teste",
            Slug = "loja-teste",
            PhoneNumber = "11999999999",
            CuisineType = "Pizza",
            MaxDeliveryRadiusKm = 5,
        });

        createStoreResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var myStoreResponse = await client.GetAsync("/api/stores/my-store");
        myStoreResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Seller_ShouldNotCreateSecondStore()
    {
        var client = _factory.CreateClient(new()
        {
            AllowAutoRedirect = false
        });

        var email = $"store.unique.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = "Seller Unique",
            Email = email,
            Password = password,
            PhoneNumber = "11988886666"
        });
        await _factory.ConfirmEmailAsync(email);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login/seller", new LoginRequestDto
        {
            Email = email,
            Password = password
        });

        var token = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);

        await client.PostAsJsonAsync("/api/stores", new CreateStoreRequestDto
        {
            Name = "Primeira Loja",
            Slug = "primeira-loja",
            PhoneNumber = "11999990000",
            CuisineType = "Lanches",
            MaxDeliveryRadiusKm = 5,
        });

        var secondCreateResponse = await client.PostAsJsonAsync("/api/stores", new CreateStoreRequestDto
        {
            Name = "Segunda Loja",
            Slug = "segunda-loja",
            PhoneNumber = "11999991111",
            CuisineType = "Japonesa",
            MaxDeliveryRadiusKm = 5,
        });

        secondCreateResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Seller_ShouldNotCreateStore_WithInvalidCuisineType()
    {
        var client = _factory.CreateClient(new()
        {
            AllowAutoRedirect = false
        });

        var email = $"store.invalid-cuisine.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = "Seller Invalid Cuisine",
            Email = email,
            Password = password,
            PhoneNumber = "11988776655"
        });
        await _factory.ConfirmEmailAsync(email);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login/seller", new LoginRequestDto
        {
            Email = email,
            Password = password
        });

        var token = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);

        var createStoreResponse = await client.PostAsJsonAsync("/api/stores", new CreateStoreRequestDto
        {
            Name = "Loja Sem Culinaria Valida",
            Slug = "loja-sem-culinaria-valida",
            PhoneNumber = "11991112222",
            CuisineType = "NaoExiste",
            MaxDeliveryRadiusKm = 5,
        });

        createStoreResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
