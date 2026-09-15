using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Urbeat.Application.DTOs;

namespace Urbeat.IntegrationTests.Infrastructure;

/// <summary>
/// Test helpers that bridge the gap introduced by RF77 (mandatory e-mail confirmation
/// before login). Legacy integration tests rely on the "register → login" shortcut;
/// after RF77 those scenarios need the account to be confirmed first.
/// </summary>
public static class TestAuthHelpers
{
    /// <summary>
    /// Marks the e-mail as confirmed for the given address. Looks the user up via
    /// <see cref="UserManager{TUser}"/> in a fresh DI scope and persists the change.
    /// Use right after a /api/auth/register/* call when you don't care about exercising
    /// the confirmation token flow (covered by EmailConfirmationFlowTests / RF77).
    /// </summary>
    public static async Task ConfirmEmailAsync<TFactory>(this TFactory factory, string email)
        where TFactory : WebApplicationFactory<Program>
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var user = await userManager.FindByEmailAsync(normalizedEmail);
        if (user is null)
        {
            throw new InvalidOperationException($"User '{normalizedEmail}' was not found for e-mail confirmation.");
        }

        if (user.EmailConfirmed)
        {
            return;
        }

        user.EmailConfirmed = true;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to confirm e-mail for '{normalizedEmail}': {string.Join(", ", result.Errors.Select(e => e.Description))}");
        }
    }

    /// <summary>
    /// Registers a seller through the public API, asserts the registration was created (so a
    /// contractor-name/e-mail conflict cannot be silently ignored), and confirms the e-mail.
    /// Integration test classes share a single in-memory database, so the supplied
    /// <paramref name="fullName"/> is made unique per call: otherwise the second test that reuses
    /// the same base name would receive a 409 contractor-name conflict and the follow-up
    /// confirmation lookup would fail with a misleading "user not found" error.
    /// </summary>
    public static async Task<HttpResponseMessage> RegisterSellerAsync<TFactory>(
        this TFactory factory,
        HttpClient client,
        string email,
        string password,
        string fullName,
        string? phoneNumber = null)
        where TFactory : WebApplicationFactory<Program>
    {
        var response = await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = $"{fullName} {Guid.NewGuid():N}",
            Email = email,
            Password = password,
            PhoneNumber = phoneNumber ?? "11988888888"
        });

        response.StatusCode.Should().Be(
            HttpStatusCode.Created,
            "seller registration must succeed; body: " + await response.Content.ReadAsStringAsync());

        await factory.ConfirmEmailAsync(email);
        return response;
    }
}
