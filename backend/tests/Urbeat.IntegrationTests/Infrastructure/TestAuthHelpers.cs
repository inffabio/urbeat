using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Urbeat.Application.DTOs;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;

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
    /// Returns an active plan that supports monthly billing. Integration tests that only need a
    /// valid seller registration use this instead of hard-coding catalog identifiers.
    /// </summary>
    public static async Task<Guid> GetDefaultPlanIdAsync<TFactory>(this TFactory factory)
        where TFactory : WebApplicationFactory<Program>
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var plan = await dbContext.Plans
            .AsNoTracking()
            .Where(x => x.IsActive && x.MonthlyEnabled)
            .OrderByDescending(x => x.Name == TestDataSeeder.DefaultPlanName)
            .ThenBy(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync();

        if (plan is null)
        {
            throw new InvalidOperationException("No active monthly subscription plan was seeded for integration tests.");
        }

        return plan.Id;
    }

    /// <summary>
    /// Posts a registration request, adding the required plan selection for seller tests that only
    /// care about the identity flow. The plan is read from the anonymous public catalog.
    /// </summary>
    public static async Task<HttpResponseMessage> PostRegistrationWithPlanAsync(
        this HttpClient client,
        string role,
        RegisterUserRequestDto request)
    {
        var effectiveRequest = request;
        if (string.Equals(role, "seller", StringComparison.OrdinalIgnoreCase) && request.PlanId is null)
        {
            var offer = await client.GetFromJsonAsync<PublicPlanOfferResponseDto>("/api/public/plans");
            var plan = offer?.Plans.FirstOrDefault(x => x.MonthlyEnabled);
            if (plan is null)
            {
                throw new InvalidOperationException("No active monthly plan is available for the seller registration test.");
            }

            effectiveRequest = new RegisterUserRequestDto
            {
                FullName = request.FullName,
                Email = request.Email,
                Password = request.Password,
                PhoneNumber = request.PhoneNumber,
                Document = request.Document,
                CurrentPassword = request.CurrentPassword,
                PlanId = plan.Id,
                BillingPeriod = SubscriptionBillingPeriod.Monthly
            };
        }

        return await client.PostAsJsonAsync($"/api/auth/register/{role}", effectiveRequest);
    }

    /// <summary>
    /// Registers a seller through the public API, asserts the registration was created, and confirms
    /// the e-mail. Integration test classes share a single in-memory database, so a unique suffix is
    /// appended to the supplied <paramref name="fullName"/> to keep test data easy to tell apart;
    /// contractor names themselves are not unique across sellers.
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
        var planId = await factory.GetDefaultPlanIdAsync();
        var response = await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = $"{fullName} {Guid.NewGuid():N}",
            Email = email,
            Password = password,
            PhoneNumber = phoneNumber ?? "11988888888",
            PlanId = planId,
            BillingPeriod = SubscriptionBillingPeriod.Monthly
        });

        response.StatusCode.Should().Be(
            HttpStatusCode.Created,
            "seller registration must succeed; body: " + await response.Content.ReadAsStringAsync());

        await factory.ConfirmEmailAsync(email);
        return response;
    }
}
