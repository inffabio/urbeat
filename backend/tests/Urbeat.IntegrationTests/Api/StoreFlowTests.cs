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
            PhoneNumber = "11999999999",
            Description = "Loja para teste de cadastro",
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
            PhoneNumber = "11999990000",
            Description = "Primeira loja do vendedor",
            CuisineType = "Lanches",
            MaxDeliveryRadiusKm = 5,
        });

        var secondCreateResponse = await client.PostAsJsonAsync("/api/stores", new CreateStoreRequestDto
        {
            Name = "Segunda Loja",
            PhoneNumber = "11999991111",
            Description = "Tentativa inválida de segunda loja",
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
            PhoneNumber = "11991112222",
            Description = "Teste de culinaria invalida",
            CuisineType = "NaoExiste",
            MaxDeliveryRadiusKm = 5,
        });

        createStoreResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
