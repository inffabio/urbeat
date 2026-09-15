using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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
    public async Task RegisterSeller_ShouldReturnConflict_WhenContractorNameAlreadyRegistered()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var baseName = $"Contratante {Guid.NewGuid():N}";

        var firstResponse = await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = baseName,
            Email = $"seller.{Guid.NewGuid():N}@urbeat.local",
            Password = "SenhaForte123",
            PhoneNumber = "11988888888"
        });
        firstResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var variations = new[]
        {
            baseName,
            baseName.ToLowerInvariant(),
            $"  {baseName}  "
        };

        foreach (var variation in variations)
        {
            var response = await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
            {
                FullName = variation,
                Email = $"seller.{Guid.NewGuid():N}@urbeat.local",
                Password = "SenhaForte123",
                PhoneNumber = "11977777777"
            });

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);

            var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
            payload.GetProperty("contractorNameAlreadyRegistered").GetBoolean().Should().BeTrue();
            payload.GetProperty("errors").GetArrayLength().Should().Be(1);
            payload.GetProperty("errors")[0].GetString().Should().Be("Nome do contratante já cadastrado.");
        }
    }

    [Fact]
    public async Task RegisterSeller_WithSameEmail_ShouldReturnEmailAlreadyExists_NotContractorNameConflict()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var email = $"seller.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";
        var fullName = $"Contratante {Guid.NewGuid():N}";

        var firstResponse = await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = fullName,
            Email = email,
            Password = password,
            PhoneNumber = "11988888888"
        });
        firstResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var secondResponse = await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = fullName,
            Email = email,
            Password = password,
            PhoneNumber = "11988888888"
        });

        secondResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var payload = await secondResponse.Content.ReadFromJsonAsync<JsonElement>();
        payload.TryGetProperty("contractorNameAlreadyRegistered", out _).Should().BeFalse();
        payload.GetProperty("errors").GetArrayLength().Should().Be(1);
        payload.GetProperty("errors")[0].GetString().Should().Be("An account with this e-mail already exists.");
    }

    [Fact]
    public async Task RegisterSeller_ShouldReturnConflict_WhenPromotingCustomerWithNameAlreadyRegistered()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var baseName = $"Contratante {Guid.NewGuid():N}";
        var customerEmail = $"customer.{Guid.NewGuid():N}@urbeat.local";

        var sellerResponse = await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = baseName,
            Email = $"seller.{Guid.NewGuid():N}@urbeat.local",
            Password = "SenhaForte123",
            PhoneNumber = "11988888888"
        });
        sellerResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var customerResponse = await client.PostAsJsonAsync("/api/auth/register/customer", new RegisterUserRequestDto
        {
            FullName = "Cliente Existente",
            Email = customerEmail,
            Password = "SenhaForte123",
            PhoneNumber = "11977777777"
        });
        customerResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var promoteResponse = await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = baseName,
            Email = customerEmail,
            Password = "SenhaForte123",
            PhoneNumber = "11977777777"
        });

        promoteResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var payload = await promoteResponse.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("contractorNameAlreadyRegistered").GetBoolean().Should().BeTrue();
        payload.GetProperty("errors")[0].GetString().Should().Be("Nome do contratante já cadastrado.");
    }

    [Fact]
    public async Task PromoteCustomerToSeller_ShouldPersistFullName_AndBlockSubsequentSellerWithSameName()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var unique = Guid.NewGuid().ToString("N");
        var customerEmail = $"customer.{unique}@urbeat.local";
        var promotedName = $"Contratante Promovido {unique}";
        const string password = "SenhaForte123";

        var customerResponse = await client.PostAsJsonAsync("/api/auth/register/customer", new RegisterUserRequestDto
        {
            FullName = "Cliente Original",
            Email = customerEmail,
            Password = password,
            PhoneNumber = "11977777777"
        });
        customerResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var promoteResponse = await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = promotedName,
            Email = customerEmail,
            Password = password,
            PhoneNumber = "11977777777"
        });
        promoteResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var secondSellerResponse = await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = promotedName,
            Email = $"seller.{unique}@urbeat.local",
            Password = password,
            PhoneNumber = "11966666666"
        });

        secondSellerResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var payload = await secondSellerResponse.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("contractorNameAlreadyRegistered").GetBoolean().Should().BeTrue();
        payload.GetProperty("errors").GetArrayLength().Should().Be(1);
        payload.GetProperty("errors")[0].GetString().Should().Be("Nome do contratante já cadastrado.");
    }

    [Fact]
    public async Task RegisterCustomer_ShouldNotBeBlockedByExistingSellerName()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var baseName = $"Contratante {Guid.NewGuid():N}";

        var sellerResponse = await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = baseName,
            Email = $"seller.{Guid.NewGuid():N}@urbeat.local",
            Password = "SenhaForte123",
            PhoneNumber = "11988888888"
        });
        sellerResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var customerResponse = await client.PostAsJsonAsync("/api/auth/register/customer", new RegisterUserRequestDto
        {
            FullName = baseName,
            Email = $"customer.{Guid.NewGuid():N}@urbeat.local",
            Password = "SenhaForte123",
            PhoneNumber = "11977777777"
        });

        customerResponse.StatusCode.Should().Be(HttpStatusCode.Created);
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
    public async Task Logout_ShouldRevokeRefreshToken_AndRejectSubsequentRefresh()
    {
        var email = $"logout.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";
        var client = _factory.CreateClient(new()
        {
            AllowAutoRedirect = false
        });

        await client.PostAsJsonAsync("/api/auth/register/customer", new RegisterUserRequestDto
        {
            FullName = "Logout Teste",
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

        var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logoutRequest.Headers.Add("Cookie", refreshCookie);
        var logoutResponse = await client.SendAsync(logoutRequest);
        logoutResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        refreshRequest.Headers.Add("Cookie", refreshCookie);
        var refreshResponse = await client.SendAsync(refreshRequest);
        refreshResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RefreshToken_ShouldRejectReplay_WhenSameTokenIsUsedTwice()
    {
        var email = $"replay.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        await client.PostAsJsonAsync("/api/auth/register/customer", new RegisterUserRequestDto
        {
            FullName = "Replay Teste",
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

        var refreshCookie = ExtractRefreshCookie(loginResponse);

        var firstRefresh = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        firstRefresh.Headers.Add("Cookie", refreshCookie);
        (await client.SendAsync(firstRefresh)).StatusCode.Should().Be(HttpStatusCode.OK);

        var replayRefresh = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        replayRefresh.Headers.Add("Cookie", refreshCookie);
        (await client.SendAsync(replayRefresh)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RefreshToken_ShouldRotate_AndRejectOldToken_WhileNewTokenWorks()
    {
        var email = $"rotate.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        await client.PostAsJsonAsync("/api/auth/register/customer", new RegisterUserRequestDto
        {
            FullName = "Rotation Teste",
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

        var oldCookie = ExtractRefreshCookie(loginResponse);

        var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        refreshRequest.Headers.Add("Cookie", oldCookie);
        var refreshResponse = await client.SendAsync(refreshRequest);
        refreshResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var newCookie = ExtractRefreshCookie(refreshResponse);
        newCookie.Should().NotBe(oldCookie);

        var oldTokenRefresh = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        oldTokenRefresh.Headers.Add("Cookie", oldCookie);
        (await client.SendAsync(oldTokenRefresh)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var newTokenRefresh = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        newTokenRefresh.Headers.Add("Cookie", newCookie);
        (await client.SendAsync(newTokenRefresh)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static string ExtractRefreshCookie(HttpResponseMessage response)
    {
        response.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        return cookies!
            .Select(static cookie => cookie.Split(';')[0])
            .Single(static cookie => cookie.StartsWith("urbeat.refresh_token="));
    }

    [Fact]
    public async Task Login_ShouldNotExposeRefreshToken_InResponseBody()
    {
        var email = $"secret.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        await client.PostAsJsonAsync("/api/auth/register/customer", new RegisterUserRequestDto
        {
            FullName = "Secret Teste",
            Email = email,
            Password = password,
            PhoneNumber = "11977777777"
        });
        await _factory.ConfirmEmailAsync(email);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login/customer", new LoginRequestDto
        {
            Email = email,
            Password = password
        });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await loginResponse.Content.ReadAsStringAsync();
        body.Should().Contain("accessToken");
        body.Should().Contain("expiresAtUtc");
        body.Should().NotContain("refreshToken");
        body.Should().NotContain("refresh_token");

        loginResponse.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        cookies!.Select(static cookie => cookie.Split(';')[0])
            .Should().Contain(static cookie => cookie.StartsWith("urbeat.refresh_token="));
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
