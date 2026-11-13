using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.IntegrationTests.Infrastructure;
using Urbeat.WebApi.Controllers;

namespace Urbeat.IntegrationTests.Api;

/// <summary>
/// End-to-end contract of the break-glass administrator recovery. The feature is hidden when
/// disabled, the key travels only in its dedicated header, successful use is one-shot and the
/// recovered account's old sessions stop working.
/// </summary>
public sealed class AdminPasswordRecoveryFlowTests
{
    private const string AdminEmail = "admin@urbeat.local";
    private const string OldPassword = "Admin12345";
    private const string NewPassword = "NovaAdmin456!";
    private const string OtherPassword = "OutraAdmin789!";
    private const string RecoveryKey = "urbeat-integration-tests-admin-recovery-key-2026";

    [Fact]
    public async Task Recover_ShouldBeHidden_WhenTheFeatureIsDisabled()
    {
        using var factory = new TestWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.SendAsync(BuildRequest(RecoveryKey, NewPassword));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Recover_ShouldRejectMissingAndInvalidKeys()
    {
        using var factory = new TestWebApplicationFactory(adminRecoveryEnabled: true, RecoveryKey);
        var client = factory.CreateClient();

        var missing = await client.SendAsync(BuildRequest(null, NewPassword));
        var invalid = await client.SendAsync(BuildRequest("chave-incorreta-mas-com-tamanho-suficiente", NewPassword));

        missing.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        invalid.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var login = await LoginAdminAsync(client, OldPassword);
        login.StatusCode.Should().Be(HttpStatusCode.OK, "an invalid key must not change the password");
    }

    [Fact]
    public async Task Recover_ShouldIgnoreAKeySentInTheBodyOrQuery()
    {
        using var factory = new TestWebApplicationFactory(adminRecoveryEnabled: true, RecoveryKey);
        var client = factory.CreateClient();

        var bodyOnly = new HttpRequestMessage(HttpMethod.Post, "/api/auth/admin/recover-password")
        {
            Content = JsonContent.Create(new
            {
                email = AdminEmail,
                newPassword = NewPassword,
                confirmPassword = NewPassword,
                adminRecoveryKey = RecoveryKey
            })
        };
        bodyOnly.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var queryOnly = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/auth/admin/recover-password?X-Admin-Recovery-Key={RecoveryKey}")
        {
            Content = JsonContent.Create(BuildBody(NewPassword))
        };
        queryOnly.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        (await client.SendAsync(bodyOnly)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.SendAsync(queryOnly)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Recover_ShouldReturnBadRequest_ForAnInvalidPayload()
    {
        using var factory = new TestWebApplicationFactory(adminRecoveryEnabled: true, RecoveryKey);
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/admin/recover-password")
        {
            Content = JsonContent.Create(new AdminRecoverPasswordRequestDto
            {
                Email = AdminEmail,
                NewPassword = "fraca",
                ConfirmPassword = "diferente"
            })
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation(AuthController.AdminRecoveryKeyHeader, RecoveryKey);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Recover_ShouldResetTheAdminPassword_AndInvalidateOldSessions()
    {
        using var factory = new TestWebApplicationFactory(adminRecoveryEnabled: true, RecoveryKey);
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var oldLogin = await LoginAdminAsync(client, OldPassword);
        oldLogin.StatusCode.Should().Be(HttpStatusCode.OK);
        var refreshCookie = ExtractRefreshCookie(oldLogin);

        var recovery = await client.SendAsync(BuildRequest(RecoveryKey, NewPassword));
        recovery.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await LoginAdminAsync(client, NewPassword)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoginAdminAsync(client, OldPassword)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        refreshRequest.Headers.Add("Cookie", refreshCookie);
        (await client.SendAsync(refreshRequest)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Recover_ShouldRejectKeyReuse_AndPreserveTheFirstRecovery()
    {
        using var factory = new TestWebApplicationFactory(adminRecoveryEnabled: true, RecoveryKey);
        var client = factory.CreateClient();

        var first = await client.SendAsync(BuildRequest(RecoveryKey, NewPassword));
        var second = await client.SendAsync(BuildRequest(RecoveryKey, OtherPassword));

        first.StatusCode.Should().Be(HttpStatusCode.NoContent);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await LoginAdminAsync(client, NewPassword)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoginAdminAsync(client, OtherPassword)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Recover_ShouldNeverAffectCustomerOrSellerAccounts()
    {
        using var factory = new TestWebApplicationFactory(adminRecoveryEnabled: true, RecoveryKey);
        var client = factory.CreateClient();
        var customerEmail = $"admin-recovery.customer.{Guid.NewGuid():N}@urbeat.local";
        var sellerEmail = $"admin-recovery.seller.{Guid.NewGuid():N}@urbeat.local";
        const string customerPassword = "Cliente123!";
        const string sellerPassword = "Lojista123!";

        (await client.PostAsJsonAsync("/api/auth/register/customer", new RegisterUserRequestDto
        {
            FullName = "Cliente Recuperação",
            Email = customerEmail,
            Password = customerPassword,
            PhoneNumber = "11977777777"
        })).StatusCode.Should().Be(HttpStatusCode.Created);
        await factory.ConfirmEmailAsync(customerEmail);

        var sellerRegistration = await factory.RegisterSellerAsync(
            client,
            sellerEmail,
            sellerPassword,
            "Lojista Recuperação");
        sellerRegistration.StatusCode.Should().Be(HttpStatusCode.Created);

        (await client.SendAsync(BuildRequest(
            RecoveryKey,
            OtherPassword,
            customerEmail))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.SendAsync(BuildRequest(
            RecoveryKey,
            OtherPassword,
            sellerEmail))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var customerLogin = await client.PostAsJsonAsync("/api/auth/login/customer", new LoginRequestDto
        {
            Email = customerEmail,
            Password = customerPassword
        });
        var sellerLogin = await client.PostAsJsonAsync("/api/auth/login/seller", new LoginRequestDto
        {
            Email = sellerEmail,
            Password = sellerPassword
        });

        customerLogin.StatusCode.Should().Be(HttpStatusCode.OK, "the customer credential must be untouched");
        sellerLogin.StatusCode.Should().Be(HttpStatusCode.OK, "the seller credential must be untouched");

        // A rejected target does not burn the key: the operator can still recover the admin.
        (await client.SendAsync(BuildRequest(RecoveryKey, NewPassword))).StatusCode
            .Should().Be(HttpStatusCode.NoContent);
    }

    private static HttpRequestMessage BuildRequest(
        string? recoveryKey,
        string newPassword,
        string email = AdminEmail)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/admin/recover-password")
        {
            Content = JsonContent.Create(BuildBody(newPassword, email))
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (recoveryKey is not null)
        {
            request.Headers.TryAddWithoutValidation(AuthController.AdminRecoveryKeyHeader, recoveryKey);
        }

        return request;
    }

    private static AdminRecoverPasswordRequestDto BuildBody(
        string newPassword,
        string email = AdminEmail) => new()
    {
        Email = email,
        NewPassword = newPassword,
        ConfirmPassword = newPassword
    };

    private static Task<HttpResponseMessage> LoginAdminAsync(HttpClient client, string password) =>
        client.PostAsJsonAsync("/api/auth/login/admin", new LoginRequestDto
        {
            Email = AdminEmail,
            Password = password
        });

    private static string ExtractRefreshCookie(HttpResponseMessage response)
    {
        response.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        return cookies!
            .Select(static cookie => cookie.Split(';')[0])
            .Single(static cookie => cookie.StartsWith("urbeat.refresh_token="));
    }
}
