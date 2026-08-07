using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Urbeat.IntegrationTests.Infrastructure;

/// <summary>
/// Seeds reference data (cuisine types, identity roles, admin user) when
/// running with InMemory database. Program.cs relies on MigrateAsync which
/// fails on InMemory, causing the built-in seeders to be skipped entirely.
/// This hosted service fills that gap so integration tests have the same
/// baseline as a real database.
/// </summary>
public sealed class TestDataSeeder : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public TestDataSeeder(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();

        await SeedRolesAsync(scope, cancellationToken);
        await SeedCuisineTypesAsync(scope, cancellationToken);
        await SeedAdminUserAsync(scope, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task SeedRolesAsync(IServiceScope scope, CancellationToken ct)
    {
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var roles = new[] { "Admin", "Seller", "Customer" };

        foreach (var roleName in roles)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
            }
        }
    }

    private static async Task SeedCuisineTypesAsync(IServiceScope scope, CancellationToken ct)
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        if (!await dbContext.CuisineTypes.AnyAsync(ct))
        {
            dbContext.CuisineTypes.AddRange(
                new CuisineType { Name = "Pizza", IsActive = true },
                new CuisineType { Name = "Lanches", IsActive = true },
                new CuisineType { Name = "Japonesa", IsActive = true },
                new CuisineType { Name = "Brasileira", IsActive = true },
                new CuisineType { Name = "Árabe", IsActive = true },
                new CuisineType { Name = "Mexicana", IsActive = true },
                new CuisineType { Name = "Doces", IsActive = true }
            );
            await dbContext.SaveChangesAsync(ct);
        }
    }

    private static async Task SeedAdminUserAsync(IServiceScope scope, CancellationToken ct)
    {
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();

        if (await userManager.FindByEmailAsync("admin@urbeat.local") is not null)
        {
            return;
        }

        var admin = new IdentityUser<Guid>
        {
            UserName = "admin@urbeat.local",
            Email = "admin@urbeat.local",
            EmailConfirmed = true
        };

        var createResult = await userManager.CreateAsync(admin, "Admin12345");
        if (createResult.Succeeded)
        {
            await userManager.AddToRoleAsync(admin, "Admin");
        }
    }
}
