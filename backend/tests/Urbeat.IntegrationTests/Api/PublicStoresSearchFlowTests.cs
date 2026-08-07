using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.IntegrationTests.Infrastructure;

namespace Urbeat.IntegrationTests.Api;

public sealed class PublicStoresSearchFlowTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public PublicStoresSearchFlowTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PublicList_ShouldFilterByCuisineType()
    {
        var sellerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });

        await RegisterLoginAndCreateStoreAsync(sellerClient, "Pizza");
        await RegisterLoginAndCreateStoreAsync(sellerClient, "Japonesa");

        var publicClient = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var filteredResponse = await publicClient.GetAsync("/api/public/stores?cuisineType=Pizza");
        filteredResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await filteredResponse.Content.ReadFromJsonAsync<List<StorePublicListItemDto>>();
        payload.Should().NotBeNull();
        payload!.Should().NotBeEmpty();
        payload.All(x => x.CuisineType == "Pizza").Should().BeTrue();
    }

    private async Task RegisterLoginAndCreateStoreAsync(HttpClient client, string cuisineType)
    {
        var email = $"store.search.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = "Seller Search",
            Email = email,
            Password = password,
            PhoneNumber = "11981112222"
        });
        await _factory.ConfirmEmailAsync(email);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login/seller", new LoginRequestDto
        {
            Email = email,
            Password = password
        });

        var token = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token!.AccessToken);

        await client.PostAsJsonAsync("/api/stores", new CreateStoreRequestDto
        {
            Name = $"Loja {cuisineType} {Guid.NewGuid():N}",
            PhoneNumber = "11983334444",
            Description = "Loja para busca por culinaria",
            CuisineType = cuisineType,
            MaxDeliveryRadiusKm = 5,
        });
    }
}
