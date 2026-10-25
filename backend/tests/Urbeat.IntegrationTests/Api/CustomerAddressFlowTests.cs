using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.IntegrationTests.Infrastructure;

namespace Urbeat.IntegrationTests.Api;

public sealed class CustomerAddressFlowTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public CustomerAddressFlowTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Customer_ShouldManageAddresses_WithLimitOfThree()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var token = await RegisterAndLoginCustomerAsync(client);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        for (var i = 1; i <= 3; i++)
        {
            var createResponse = await client.PostAsJsonAsync("/api/customer/addresses", new UpsertCustomerAddressRequestDto
            {
                Cep = "01001000",
                Number = i.ToString(),
                Street = "Rua Teste",
                Neighborhood = "Centro",
                City = "Sao Paulo",
                State = "SP",
                IsPrimary = i == 1
            });

            createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var conflictResponse = await client.PostAsJsonAsync("/api/customer/addresses", new UpsertCustomerAddressRequestDto
        {
            Cep = "01001000",
            Number = "4",
            Street = "Rua Teste",
            Neighborhood = "Centro",
            City = "Sao Paulo",
            State = "SP"
        });

        conflictResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var listResponse = await client.GetAsync("/api/customer/addresses");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var listPayload = await listResponse.Content.ReadFromJsonAsync<List<CustomerAddressResponseDto>>();
        listPayload.Should().NotBeNull();
        listPayload!.Should().HaveCount(3);
    }

    [Fact]
    public async Task Customer_ShouldPersistAddressCoordinates_AndKeepThemWhenUpdateOmitsThem()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var token = await RegisterAndLoginCustomerAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createResponse = await client.PostAsJsonAsync("/api/customer/addresses", new UpsertCustomerAddressRequestDto
        {
            Cep = "01001000",
            Number = "100",
            Street = "Rua Teste",
            Neighborhood = "Centro",
            City = "Sao Paulo",
            State = "SP",
            IsPrimary = true,
            Latitude = -23.55052,
            Longitude = -46.633308
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<CustomerAddressResponseDto>();
        created!.Latitude.Should().Be(-23.55052);
        created.Longitude.Should().Be(-46.633308);

        var updateResponse = await client.PutAsJsonAsync($"/api/customer/addresses/{created.Id}",
            new UpsertCustomerAddressRequestDto
            {
                Cep = "01001000",
                Number = "101",
                Street = "Rua Teste",
                Neighborhood = "Centro",
                City = "Sao Paulo",
                State = "SP",
                IsPrimary = true
            });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var list = await client.GetFromJsonAsync<List<CustomerAddressResponseDto>>("/api/customer/addresses");
        var updated = list!.Single(x => x.Id == created.Id);
        updated.Number.Should().Be("101");
        updated.Latitude.Should().Be(-23.55052, "editing the address text must not drop the geocode");
        updated.Longitude.Should().Be(-46.633308);
    }

    [Theory]
    [InlineData(-23.55, null)]
    [InlineData(null, -46.63)]
    [InlineData(91.0, -46.63)]
    [InlineData(-23.55, 181.0)]
    public async Task Customer_ShouldRejectPartialOrOutOfRangeCoordinates(double? latitude, double? longitude)
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var token = await RegisterAndLoginCustomerAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/customer/addresses", new UpsertCustomerAddressRequestDto
        {
            Cep = "01001000",
            Number = "100",
            Street = "Rua Teste",
            Neighborhood = "Centro",
            City = "Sao Paulo",
            State = "SP",
            Latitude = latitude,
            Longitude = longitude
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CepLookup_ShouldReturnBadRequest_ForInvalidCep()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync("/api/address-lookup/cep/123");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<string> RegisterAndLoginCustomerAsync(HttpClient client)
    {
        var email = $"customer.address.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        await client.PostAsJsonAsync("/api/auth/register/customer", new RegisterUserRequestDto
        {
            FullName = "Customer Address",
            Email = email,
            Password = password,
            PhoneNumber = "11980001111"
        });
        await _factory.ConfirmEmailAsync(email);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login/customer", new LoginRequestDto
        {
            Email = email,
            Password = password
        });

        var token = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        return token!.AccessToken;
    }
}
