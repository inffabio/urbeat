using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.IntegrationTests.Infrastructure;

namespace Urbeat.IntegrationTests.Api;

public sealed class EmailChangeChallengeFlowTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public EmailChangeChallengeFlowTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task UpdateEmail_ShouldRejectAnonymousChange_WithoutRegistrationChallenge()
    {
        var victimEmail = $"victim.{Guid.NewGuid():N}@urbeat.local";
        var registerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });

        await registerClient.PostAsJsonAsync("/api/auth/register/customer", new RegisterUserRequestDto
        {
            FullName = "Victim",
            Email = victimEmail,
            Password = "SenhaForte123",
            PhoneNumber = "11977777777"
        });

        var attackerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var response = await attackerClient.PostAsJsonAsync("/api/auth/update-email", new UpdateEmailRequestDto
        {
            CurrentEmail = victimEmail,
            NewEmail = $"attacker.{Guid.NewGuid():N}@urbeat.local",
            EmailChangeChallenge = "not-a-valid-challenge"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateEmail_ShouldSucceed_WithRegistrationChallenge_AndBeSingleUse()
    {
        var email = $"onboarding.{Guid.NewGuid():N}@urbeat.local";
        var newEmail = $"onboarding.new.{Guid.NewGuid():N}@urbeat.local";
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var registerResponse = await client.PostAsJsonAsync("/api/auth/register/customer", new RegisterUserRequestDto
        {
            FullName = "Onboarding",
            Email = email,
            Password = "SenhaForte123",
            PhoneNumber = "11977777777"
        });
        registerResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var registerPayload = await registerResponse.Content.ReadFromJsonAsync<JsonElement>();
        var challenge = registerPayload.GetProperty("emailChangeChallenge").GetString();
        challenge.Should().NotBeNullOrWhiteSpace();

        var updateResponse = await client.PostAsJsonAsync("/api/auth/update-email", new UpdateEmailRequestDto
        {
            CurrentEmail = email,
            NewEmail = newEmail,
            EmailChangeChallenge = challenge!
        });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // One-use: replaying the same challenge must fail.
        var replayResponse = await client.PostAsJsonAsync("/api/auth/update-email", new UpdateEmailRequestDto
        {
            CurrentEmail = newEmail,
            NewEmail = $"another.{Guid.NewGuid():N}@urbeat.local",
            EmailChangeChallenge = challenge!
        });
        replayResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateEmail_ShouldReject_WhenChallengeBoundToAnotherEmail()
    {
        var email = $"bound.{Guid.NewGuid():N}@urbeat.local";
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var registerResponse = await client.PostAsJsonAsync("/api/auth/register/customer", new RegisterUserRequestDto
        {
            FullName = "Bound",
            Email = email,
            Password = "SenhaForte123",
            PhoneNumber = "11977777777"
        });

        var registerPayload = await registerResponse.Content.ReadFromJsonAsync<JsonElement>();
        var challenge = registerPayload.GetProperty("emailChangeChallenge").GetString();

        var response = await client.PostAsJsonAsync("/api/auth/update-email", new UpdateEmailRequestDto
        {
            CurrentEmail = "someone.else@urbeat.local",
            NewEmail = $"moved.{Guid.NewGuid():N}@urbeat.local",
            EmailChangeChallenge = challenge!
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
