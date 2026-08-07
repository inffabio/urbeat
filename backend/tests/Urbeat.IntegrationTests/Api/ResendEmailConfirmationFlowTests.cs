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
        body.GetProperty("succeeded").GetBoolean().Should().BeTrue();
        body.GetProperty("alreadyConfirmed").GetBoolean().Should().BeFalse();

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
        _factory.EmailService.FindLastByRecipient(email).Should().NotBeNull();
    }

    private static string NewEmail(string prefix) => $"{prefix}.{Guid.NewGuid():N}@urbeat.test";

    private static string ExtractConfirmLink(string html)
    {
        var match = Regex.Match(html, "/c/(?<code>[a-zA-Z0-9_-]{6,})");
        match.Success.Should().BeTrue("the confirmation email must contain the shortCode");
        return match.Groups["code"].Value;
    }
}
