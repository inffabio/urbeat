using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.IntegrationTests.Infrastructure;

namespace Urbeat.IntegrationTests.Api;

public sealed class StoreAddressFlowTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public StoreAddressFlowTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Seller_ShouldUpsertAddress_AndGetAddress()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (token, storeId) = await RegisterLoginAndCreateStoreAsync(client, "Pizza");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var upsertResponse = await client.PutAsJsonAsync($"/api/stores/{storeId}/address", new UpdateStoreAddressRequestDto
        {
            Street = "Rua das Flores",
            Number = "123",
            Neighborhood = "Centro",
            City = "Sao Paulo",
            State = "SP",
            ZipCode = "01001000",
            Reference = "Perto da praca"
        });

        upsertResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var getResponse = await client.GetAsync($"/api/stores/{storeId}/address");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await getResponse.Content.ReadFromJsonAsync<StoreAddressResponseDto>();
        payload.Should().NotBeNull();
        payload!.City.Should().Be("Sao Paulo");
    }

    [Fact]
    public async Task Seller_ShouldNotUpsertAddress_FromAnotherSellerStore()
    {
        var ownerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (_, storeId) = await RegisterLoginAndCreateStoreAsync(ownerClient, "Lanches");

        var attackerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (attackerToken, _) = await RegisterLoginAndCreateStoreAsync(attackerClient, "Japonesa");
        attackerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", attackerToken);

        var upsertResponse = await attackerClient.PutAsJsonAsync($"/api/stores/{storeId}/address", new UpdateStoreAddressRequestDto
        {
            Street = "Rua Invalida",
            Number = "1",
            Neighborhood = "Bairro",
            City = "Cidade",
            State = "SP",
            ZipCode = "00000000"
        });

        upsertResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<(string AccessToken, Guid StoreId)> RegisterLoginAndCreateStoreAsync(HttpClient client, string cuisineType)
    {
        var email = $"store.address.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = "Seller Address",
            Email = email,
            Password = password,
            PhoneNumber = "11980000000"
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
            Name = "Loja Endereco",
            PhoneNumber = "11987776666",
            Description = "Loja para teste de endereco",
            CuisineType = cuisineType,
            MaxDeliveryRadiusKm = 5,
        });

        var store = await createStoreResponse.Content.ReadFromJsonAsync<StoreResponseDto>();
        return (accessToken, store!.Id);
    }
}
