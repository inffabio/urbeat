using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

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
}
