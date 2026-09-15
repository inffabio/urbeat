using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.IntegrationTests.Infrastructure;

namespace Urbeat.IntegrationTests.Api;

public sealed class SellerProfileFlowTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public SellerProfileFlowTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task UpdateSellerProfile_ShouldPersistFullNameClaim_ForAuthenticatedSeller()
    {
        var email = $"seller.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";
        var client = await CreateAuthenticatedSellerAsync(email, password, "Contratante Inicial");

        var updateResponse = await client.PutAsJsonAsync("/api/seller/profile", new UpdateSellerProfileRequestDto
        {
            FullName = "Contratante Atualizado"
        });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResponse.Content.ReadFromJsonAsync<JsonElement>();
        updated.GetProperty("fullName").GetString().Should().Be("Contratante Atualizado");
        updated.GetProperty("email").GetString().Should().Be(email);

        var profileResponse = await client.GetAsync("/api/seller/profile");
        profileResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var profile = await profileResponse.Content.ReadFromJsonAsync<JsonElement>();
        profile.GetProperty("fullName").GetString().Should().Be("Contratante Atualizado");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ab")]
    public async Task UpdateSellerProfile_ShouldRejectInvalidNames(string name)
    {
        var email = $"seller.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";
        var client = await CreateAuthenticatedSellerAsync(email, password, "Contratante Valido");

        var response = await client.PutAsJsonAsync("/api/seller/profile", new UpdateSellerProfileRequestDto
        {
            FullName = name
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateSellerProfile_ShouldRejectNamesLongerThanOneHundredTwentyCharacters()
    {
        var email = $"seller.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";
        var client = await CreateAuthenticatedSellerAsync(email, password, "Contratante Valido");

        var response = await client.PutAsJsonAsync("/api/seller/profile", new UpdateSellerProfileRequestDto
        {
            FullName = new string('a', 121)
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateSellerProfile_ShouldBeForbiddenForCustomers()
    {
        var email = $"customer.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var registerResponse = await client.PostAsJsonAsync("/api/auth/register/customer", new RegisterUserRequestDto
        {
            FullName = "Cliente Teste",
            Email = email,
            Password = password,
            PhoneNumber = "11999999999"
        });
        registerResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        await _factory.ConfirmEmailAsync(email);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login/customer", new LoginRequestDto
        {
            Email = email,
            Password = password
        });
        var token = (await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponseDto>())!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PutAsJsonAsync("/api/seller/profile", new UpdateSellerProfileRequestDto
        {
            FullName = "Contratante Indevido"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<HttpClient> CreateAuthenticatedSellerAsync(string email, string password, string fullName)
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var registerResponse = await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = $"{fullName} {Guid.NewGuid():N}",
            Email = email,
            Password = password,
            PhoneNumber = "11988888888"
        });
        registerResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        await _factory.ConfirmEmailAsync(email);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login/seller", new LoginRequestDto
        {
            Email = email,
            Password = password
        });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var token = (await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponseDto>())!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
