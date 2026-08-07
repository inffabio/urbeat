using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.IntegrationTests.Infrastructure;

namespace Urbeat.IntegrationTests.Api;

public sealed class PublicStoresFlowTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public PublicStoresFlowTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PublicEndpoints_ShouldListAndGetStoreDetails()
    {
        var sellerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (token, storeId) = await RegisterLoginAndCreateStoreAsync(sellerClient, "Pizza");
        sellerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        await sellerClient.PutAsJsonAsync($"/api/stores/{storeId}/address", new UpdateStoreAddressRequestDto
        {
            Street = "Rua Publica",
            Number = "99",
            Neighborhood = "Centro",
            City = "Sao Paulo",
            State = "SP",
            ZipCode = "01001000"
        });

        await sellerClient.PutAsJsonAsync($"/api/stores/{storeId}/business-hours", new UpsertStoreBusinessHoursRequestDto
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
                }
            ]
        });

        var publicClient = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var listResponse = await publicClient.GetAsync("/api/public/stores");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var listPayload = await listResponse.Content.ReadFromJsonAsync<List<StorePublicListItemDto>>();
        listPayload.Should().NotBeNull();
        listPayload!.Any(x => x.Id == storeId).Should().BeTrue();

        var detailsResponse = await publicClient.GetAsync($"/api/public/stores/{storeId}");
        detailsResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var detailsPayload = await detailsResponse.Content.ReadFromJsonAsync<StorePublicDetailsDto>();
        detailsPayload.Should().NotBeNull();
        detailsPayload!.Id.Should().Be(storeId);
        detailsPayload.Address.Should().NotBeNull();
        detailsPayload.BusinessHours.Should().HaveCount(1);
    }

    private async Task<(string AccessToken, Guid StoreId)> RegisterLoginAndCreateStoreAsync(HttpClient client, string cuisineType)
    {
        var email = $"store.public.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = "Seller Public",
            Email = email,
            Password = password,
            PhoneNumber = "11983332222"
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
            Name = "Loja Publica",
            PhoneNumber = "11989998888",
            Description = "Loja para consulta publica",
            CuisineType = cuisineType,
            MaxDeliveryRadiusKm = 5,
        });

        var store = await createStoreResponse.Content.ReadFromJsonAsync<StoreResponseDto>();
        return (accessToken, store!.Id);
    }
}
