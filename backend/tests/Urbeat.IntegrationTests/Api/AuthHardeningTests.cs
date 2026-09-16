using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Domain.Entities;
using Urbeat.IntegrationTests.Infrastructure;
using Urbeat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Urbeat.IntegrationTests.Api;

public sealed class AuthHardeningTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public AuthHardeningTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ForgotPassword_ShouldReturnUniformResponse_ForExistingAndUnknownEmail()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"forgot.{Guid.NewGuid():N}@urbeat.local";

        await client.PostAsJsonAsync("/api/auth/register/customer", new RegisterUserRequestDto
        {
            FullName = "Forgot Teste",
            Email = email,
            Password = "SenhaForte123",
            PhoneNumber = "11977777777"
        });
        await _factory.ConfirmEmailAsync(email);

        var knownResponse = await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequestDto { Email = email });
        var unknownResponse = await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequestDto { Email = $"missing.{Guid.NewGuid():N}@urbeat.local" });

        knownResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        unknownResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var knownBody = await knownResponse.Content.ReadAsStringAsync();
        var unknownBody = await unknownResponse.Content.ReadAsStringAsync();
        knownBody.Should().Be(unknownBody);
        knownBody.Should().NotContain("\"found\"");
    }

    [Fact]
    public async Task ResetPassword_ShouldRevokeActiveRefreshTokens()
    {
        var email = $"reset.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";
        const string newPassword = "NovaSenhaForte456!";
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        await client.PostAsJsonAsync("/api/auth/register/customer", new RegisterUserRequestDto
        {
            FullName = "Reset Teste",
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
        var refreshCookie = ExtractRefreshCookie(loginResponse);

        var rawToken = $"reset-{Guid.NewGuid():N}";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
            var user = await userManager.FindByEmailAsync(email);
            user.Should().NotBeNull();

            db.PasswordResetTokens.Add(new PasswordResetToken
            {
                UserId = user!.Id,
                TokenHash = userManager.PasswordHasher.HashPassword(user, rawToken),
                ExpiresAt = DateTime.UtcNow.AddHours(1),
                Used = false
            });
            await db.SaveChangesAsync();
        }

        var resetResponse = await client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequestDto
        {
            Token = rawToken,
            NewPassword = newPassword,
            ConfirmPassword = newPassword
        });
        var resetBody = await resetResponse.Content.ReadAsStringAsync();
        resetResponse.StatusCode.Should().Be(HttpStatusCode.OK, resetBody);

        var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        refreshRequest.Headers.Add("Cookie", refreshCookie);
        var refreshResponse = await client.SendAsync(refreshRequest);

        refreshResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static string ExtractRefreshCookie(HttpResponseMessage response)
    {
        response.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        return cookies!
            .Select(static cookie => cookie.Split(';')[0])
            .Single(static cookie => cookie.StartsWith("urbeat.refresh_token="));
    }
}
