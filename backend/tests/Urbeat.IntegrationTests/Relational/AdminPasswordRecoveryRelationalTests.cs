using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Repositories;
using Urbeat.Infrastructure.Identity;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Persistence.Repositories;
using Urbeat.Infrastructure.Persistence.UnitOfWork;
using Xunit.Abstractions;

namespace Urbeat.IntegrationTests.Relational;

/// <summary>
/// The one-shot guarantee of the break-glass admin recovery is enforced by the PostgreSQL unique
/// index on <c>AdminRecoveryKeyUsages.KeyFingerprint</c> together with
/// <c>INSERT ... ON CONFLICT DO NOTHING</c> inside the password-change transaction. The InMemory
/// provider cannot reproduce that concurrency semantics, so this test drives two real concurrent
/// recoveries with the same key against PostgreSQL. Skipped when Docker is unavailable, matching
/// the other Relational tests.
/// </summary>
public sealed class AdminPasswordRecoveryRelationalTests
{
    private const string AdminEmail = "admin@urbeat.local";
    private const string OldPassword = "Admin12345";
    private const string NewPassword = "NovaAdmin456!";
    private const string OtherPassword = "OutraAdmin789!";
    private const string RecoveryKey = "urbeat-relational-admin-recovery-key-2026";

    private readonly ITestOutputHelper _output;

    public AdminPasswordRecoveryRelationalTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task ConcurrentRecoveries_ShouldAllowExactlyOneSuccess_AndRejectTheKeyReuse()
    {
        var container = await StartPostgresOrSkipAsync();
        if (container is null)
        {
            return;
        }

        try
        {
            await using var provider = await BuildProviderAsync(container);

            await using var firstScope = provider.CreateAsyncScope();
            await using var secondScope = provider.CreateAsyncScope();

            var firstService = firstScope.ServiceProvider.GetRequiredService<IAdminPasswordRecoveryService>();
            var secondService = secondScope.ServiceProvider.GetRequiredService<IAdminPasswordRecoveryService>();

            var results = await Task.WhenAll(
                firstService.RecoverAsync(BuildRequest(NewPassword), RecoveryKey, "203.0.113.10"),
                secondService.RecoverAsync(BuildRequest(OtherPassword), RecoveryKey, "203.0.113.11"));

            results.Count(result => result.Status == AdminPasswordRecoveryStatus.Succeeded).Should().Be(1);
            results.Count(result => result.Status == AdminPasswordRecoveryStatus.KeyAlreadyUsed).Should().Be(1);

            var winnerPassword = results[0].Status == AdminPasswordRecoveryStatus.Succeeded
                ? NewPassword
                : OtherPassword;
            var loserPassword = winnerPassword == NewPassword ? OtherPassword : NewPassword;

            await using var assertionScope = provider.CreateAsyncScope();
            var db = assertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userManager = assertionScope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();

            var usages = await db.AdminRecoveryKeyUsages.ToListAsync();
            usages.Should().ContainSingle(
                "the unique index must keep a single row for the key fingerprint across concurrent attempts");

            var admin = await userManager.FindByEmailAsync(AdminEmail);
            admin.Should().NotBeNull();
            (await userManager.CheckPasswordAsync(admin!, winnerPassword)).Should().BeTrue(
                "the winning recovery must have applied its password");
            (await userManager.CheckPasswordAsync(admin!, loserPassword)).Should().BeFalse(
                "the rejected concurrent attempt must not change the password");
            (await userManager.CheckPasswordAsync(admin!, OldPassword)).Should().BeFalse();
        }
        finally
        {
            await container.DisposeAsync();
        }
    }

    private static AdminRecoverPasswordRequestDto BuildRequest(string newPassword) => new()
    {
        Email = AdminEmail,
        NewPassword = newPassword,
        ConfirmPassword = newPassword
    };

    private static async Task<ServiceProvider> BuildProviderAsync(PostgreSqlContainer container)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(container.GetConnectionString()));

        services.AddIdentityCore<IdentityUser<Guid>>(options =>
            {
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredLength = 8;
                options.User.RequireUniqueEmail = true;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        services.Configure<AdminRecoveryOptions>(options =>
        {
            options.Enabled = true;
            options.Key = RecoveryKey;
        });
        services.Configure<AdminSeedOptions>(options => options.Email = AdminEmail);
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IEfUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IAdminPasswordRecoveryService, AdminPasswordRecoveryService>();

        var provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.EnsureCreatedAsync();

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();

        foreach (var roleName in new[] { "Admin", "Customer", "Seller" })
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
            }
        }

        var admin = new IdentityUser<Guid>
        {
            UserName = AdminEmail,
            Email = AdminEmail,
            EmailConfirmed = true
        };
        (await userManager.CreateAsync(admin, OldPassword)).Succeeded.Should().BeTrue();
        (await userManager.AddToRoleAsync(admin, "Admin")).Succeeded.Should().BeTrue();

        return provider;
    }

    private async Task<PostgreSqlContainer?> StartPostgresOrSkipAsync()
    {
        PostgreSqlContainer? container = null;

        try
        {
            container = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .WithDatabase("urbeat")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();

            await container.StartAsync();
            return container;
        }
        catch (Exception exception)
        {
            if (container is not null)
            {
                await container.DisposeAsync();
            }

            _output.WriteLine($"SKIPPED (Docker unavailable): {exception.Message}");
            return null;
        }
    }
}
