using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.IntegrationTests.Infrastructure;

namespace Urbeat.IntegrationTests.Api;

public sealed class AuthenticationFlowTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public AuthenticationFlowTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task RegisterCustomer_LoginCustomer_AndAccessCustomerEndpoint_ShouldSucceed()
    {
        var email = $"customer.{Guid.NewGuid():N}@urbeat.local";
        var password = "SenhaForte123";
        var client = _factory.CreateClient(new()
        {
            AllowAutoRedirect = false
        });

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
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var tokenPayload = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        tokenPayload.Should().NotBeNull();
        tokenPayload!.AccessToken.Should().NotBeNullOrWhiteSpace();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenPayload.AccessToken);
        var protectedResponse = await client.GetAsync("/api/customer/home");
        protectedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RegisterSeller_LoginSeller_AndAccessCustomerEndpoint_ShouldReturnForbidden()
    {
        var email = $"seller.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";
        var client = _factory.CreateClient(new()
        {
            AllowAutoRedirect = false
        });

        var registerResponse = await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = "Vendedor Teste",
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

        var tokenPayload = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        tokenPayload.Should().NotBeNull();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenPayload!.AccessToken);
        var protectedResponse = await client.GetAsync("/api/customer/home");
        protectedResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RefreshToken_ShouldReturnNewAccessToken_WhenCookieIsValid()
    {
        var email = $"refresh.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";
        var client = _factory.CreateClient(new()
        {
            AllowAutoRedirect = false
        });

        await client.PostAsJsonAsync("/api/auth/register/customer", new RegisterUserRequestDto
        {
            FullName = "Refresh Teste",
            Email = email,
            Password = password
        });
        await _factory.ConfirmEmailAsync(email);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login/customer", new LoginRequestDto
        {
            Email = email,
            Password = password
        });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        loginResponse.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        var refreshCookie = cookies!
            .Select(static cookie => cookie.Split(';')[0])
            .Single(static cookie => cookie.StartsWith("urbeat.refresh_token="));

        var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        refreshRequest.Headers.Add("Cookie", refreshCookie);

        var refreshResponse = await client.SendAsync(refreshRequest);
        refreshResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var refreshPayload = await refreshResponse.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        refreshPayload.Should().NotBeNull();
        refreshPayload!.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Login_ShouldReturnLocked_WhenMaxFailedAttemptsIsReached()
    {
        var email = $"lockout.{Guid.NewGuid():N}@urbeat.local";
        const string validPassword = "SenhaForte123";
        const string invalidPassword = "SenhaErrada123";
        var client = _factory.CreateClient(new()
        {
            AllowAutoRedirect = false
        });

        await client.PostAsJsonAsync("/api/auth/register/customer", new RegisterUserRequestDto
        {
            FullName = "Lockout Teste",
            Email = email,
            Password = validPassword
        });
        await _factory.ConfirmEmailAsync(email);

        HttpStatusCode lastStatus = HttpStatusCode.OK;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login/customer", new LoginRequestDto
            {
                Email = email,
                Password = invalidPassword
            });

            lastStatus = response.StatusCode;
        }

        lastStatus.Should().Be(HttpStatusCode.Locked);

        var validAfterLockResponse = await client.PostAsJsonAsync("/api/auth/login/customer", new LoginRequestDto
        {
            Email = email,
            Password = validPassword
        });

        validAfterLockResponse.StatusCode.Should().Be(HttpStatusCode.Locked);
    }
}
