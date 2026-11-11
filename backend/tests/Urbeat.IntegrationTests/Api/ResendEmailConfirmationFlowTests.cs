using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.IntegrationTests.Infrastructure;

namespace Urbeat.IntegrationTests.Api;

/// <summary>
/// Integration tests for RF77 - Resend confirmation e-mail.
/// </summary>
public sealed class ResendEmailConfirmationFlowTests : IClassFixture<EmailConfirmationTestWebApplicationFactory>
{
    private readonly EmailConfirmationTestWebApplicationFactory _factory;

    public ResendEmailConfirmationFlowTests(EmailConfirmationTestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Resend_ShouldReturn200_AndSendANewEmail_WhenUserExistsAndIsPending()
    {
        _factory.EmailService.Clear();
        var email = NewEmail("resend.pending");
        const string password = "SenhaForte123";
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        await client.PostAsJsonAsync("/api/auth/register/customer", new RegisterUserRequestDto
        {
            FullName = "Resend Pending RF77",
            Email = email,
            Password = password
        });

        // Initial email already in the fake. Clear and request resend.
        _factory.EmailService.Clear();

        var resendResponse = await client.PostAsJsonAsync("/api/auth/email/resend-confirmation", new ResendEmailConfirmationRequestDto
        {
            Email = email
        });

        resendResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resendResponse.Content.ReadFromJsonAsync<JsonElement>();
        ShouldBeGenericAcknowledgement(body);

        // The response only acknowledges an accepted/enqueued request. The deadline is a
        // server-calculated UX hint so the page can restart its cooldown consistently.
        DateTimeOffset.TryParse(
            body.GetProperty("resendAvailableAtUtc").GetString(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var resendAvailableAtUtc).Should().BeTrue();
        resendAvailableAtUtc.UtcDateTime.Should().BeAfter(DateTime.UtcNow);

        await _factory.DispatchOutboxAsync();

        var resent = _factory.EmailService.FindLastByRecipient(email);
        resent.Should().NotBeNull("a new confirmation e-mail must be issued on resend");
        resent!.HtmlBody.Should().Contain("/c/");
    }

    [Fact]
    public async Task Resend_ShouldReturn200_AndNotSendEmail_WhenUserIsAlreadyConfirmed()
    {
        _factory.EmailService.Clear();
        var email = NewEmail("resend.confirmed");
        const string password = "SenhaForte123";
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var registerResponse = await client.PostAsJsonAsync("/api/auth/register/customer", new RegisterUserRequestDto
        {
            FullName = "Resend Confirmed RF77",
            Email = email,
            Password = password
        });
        registerResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // Extract token from the initial email and confirm.
        await _factory.DispatchOutboxAsync();
        var initial = _factory.EmailService.FindLastByRecipient(email)!;
        var code = ExtractConfirmLink(initial.HtmlBody);
        var confirmResponse = await client.PostAsync($"/api/auth/email/confirm/{code}", null);
        confirmResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        _factory.EmailService.Clear();

        var resendResponse = await client.PostAsJsonAsync("/api/auth/email/resend-confirmation", new ResendEmailConfirmationRequestDto
        {
            Email = email
        });

        resendResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resendResponse.Content.ReadFromJsonAsync<JsonElement>();
        // Privacy: an already-confirmed address must be indistinguishable from an unknown or
        // pending one, so the confirmation state is never part of the public response.
        ShouldBeGenericAcknowledgement(body);
        _factory.EmailService.FindLastByRecipient(email).Should().BeNull("the service must not resend e-mails for confirmed accounts");
    }

    [Fact]
    public async Task Resend_ShouldReturn200_ButNotSendEmail_WhenEmailDoesNotExist()
    {
        _factory.EmailService.Clear();
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var unknown = NewEmail("ghost");

        var response = await client.PostAsJsonAsync("/api/auth/email/resend-confirmation", new ResendEmailConfirmationRequestDto
        {
            Email = unknown
        });

        // Privacy: always returns 200 OK, but no email is sent.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        ShouldBeGenericAcknowledgement(body);
        _factory.EmailService.FindLastByRecipient(unknown).Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public async Task Resend_ShouldReturnBadRequest_WhenEmailIsInvalid(string email)
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.PostAsJsonAsync("/api/auth/email/resend-confirmation", new ResendEmailConfirmationRequestDto
        {
            Email = email
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Resend_ShouldBeCaseInsensitive_OnEmailLookup()
    {
        _factory.EmailService.Clear();
        var email = NewEmail("case");
        const string password = "SenhaForte123";
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        await client.PostAsJsonAsync("/api/auth/register/customer", new RegisterUserRequestDto
        {
            FullName = "Case Insensitive RF77",
            Email = email,
            Password = password
        });
        _factory.EmailService.Clear();

        var response = await client.PostAsJsonAsync("/api/auth/email/resend-confirmation", new ResendEmailConfirmationRequestDto
        {
            Email = email.ToUpperInvariant()
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.DispatchOutboxAsync();
        _factory.EmailService.FindLastByRecipient(email).Should().NotBeNull();
    }

    [Fact]
    public async Task Resend_ShouldReturnTooManyRequests_WhenTheRateLimitIsExceeded()
    {
        using var factory = new EmailConfirmationTestWebApplicationFactory(
            rolePromotionPermitLimit: 10000,
            emailResendPermitLimit: 1);
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var request = new ResendEmailConfirmationRequestDto
        {
            Email = NewEmail("rate-limit"),
        };

        var first = await client.PostAsJsonAsync("/api/auth/email/resend-confirmation", request);
        var second = await client.PostAsJsonAsync("/api/auth/email/resend-confirmation", request);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    /// <summary>
    /// Asserts the public privacy contract of the resend endpoint: unknown, pending and
    /// already-confirmed addresses must yield the same acknowledgement and the same shape.
    /// </summary>
    private static void ShouldBeGenericAcknowledgement(JsonElement body)
    {
        body.TryGetProperty("alreadyConfirmed", out _).Should().BeFalse(
            "the public resend contract must not disclose whether the e-mail is unknown, pending or confirmed");
        body.EnumerateObject().Select(property => property.Name).Should()
            .BeEquivalentTo("succeeded", "message", "resendAvailableAtUtc");
        body.GetProperty("succeeded").GetBoolean().Should().BeTrue();
        body.GetProperty("message").GetString().Should()
            .Be("Se o e-mail estiver cadastrado, um link de confirmação foi enviado.");
        body.GetProperty("resendAvailableAtUtc").ValueKind.Should().Be(JsonValueKind.String);
    }

    private static string NewEmail(string prefix) => $"{prefix}.{Guid.NewGuid():N}@urbeat.test";

    private static string ExtractConfirmLink(string html)
    {
        var match = Regex.Match(html, "/c/(?<code>[a-zA-Z0-9_-]{6,})");
        match.Success.Should().BeTrue("the confirmation email must contain the shortCode");
        return match.Groups["code"].Value;
    }
}
