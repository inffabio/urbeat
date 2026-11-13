using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Domain.Repositories;
using Urbeat.Infrastructure.Identity;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Persistence.Repositories;
using Urbeat.Infrastructure.Persistence.UnitOfWork;

namespace Urbeat.UnitTests.Infrastructure.Identity;

/// <summary>
/// The break-glass admin recovery must stay disabled by default, accept the key only from the
/// dedicated header, never expose or persist it, target only the configured administrator, burn the
/// key after one successful use and revoke every session for that account.
/// </summary>
public sealed class AdminPasswordRecoveryServiceTests
{
    private const string AdminEmail = "admin@urbeat.local";
    private const string CustomerEmail = "cliente@urbeat.local";
    private const string OldPassword = "OldAdmin123!";
    private const string NewPassword = "NewAdmin456!";
    private const string OtherPassword = "OtherAdmin789!";
    private const string RecoveryKey = "urbeat-unit-tests-admin-recovery-key-2026";
    private const string WrongKey = "urbeat-unit-tests-wrong-recovery-key-2026";

    [Fact]
    public async Task RecoverAsync_ShouldAnswerNotAvailable_WhenTheFeatureIsDisabled()
    {
        await using var provider = await CreateProviderAsync(enabled: false);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAdminPasswordRecoveryService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var result = await service.RecoverAsync(BuildRequest(NewPassword), RecoveryKey, "203.0.113.10");

        result.Status.Should().Be(AdminPasswordRecoveryStatus.NotAvailable);
        service.IsAvailable.Should().BeFalse();
        (await db.AdminRecoveryKeyUsages.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RecoverAsync_ShouldAnswerNotAvailable_WhenTheConfiguredKeyIsTooShort()
    {
        await using var provider = await CreateProviderAsync(key: "short-key");
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAdminPasswordRecoveryService>();

        service.IsAvailable.Should().BeFalse();
        (await service.RecoverAsync(BuildRequest(NewPassword), "short-key", null)).Status
            .Should().Be(AdminPasswordRecoveryStatus.NotAvailable);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData(WrongKey)]
    public async Task RecoverAsync_ShouldRejectAMissingOrWrongKey(string? providedKey)
    {
        await using var provider = await CreateProviderAsync();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAdminPasswordRecoveryService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var result = await service.RecoverAsync(BuildRequest(NewPassword), providedKey, "203.0.113.10");

        result.Status.Should().Be(AdminPasswordRecoveryStatus.InvalidKey);
        (await db.AdminRecoveryKeyUsages.CountAsync()).Should().Be(0);
        var admin = await userManager.FindByEmailAsync(AdminEmail);
        (await userManager.CheckPasswordAsync(admin!, OldPassword)).Should().BeTrue(
            "an invalid key must not change the password");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task RecoverAsync_ShouldRejectTheRequest_WhenTheConfiguredAdminEmailIsMissing(string? configuredEmail)
    {
        await using var provider = await CreateProviderAsync(configuredAdminEmail: configuredEmail);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAdminPasswordRecoveryService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // The valid key is not enough: without AdminSeed:Email there is no legitimate target, so
        // the request must not fall back to "any Admin account".
        var adminResult = await service.RecoverAsync(BuildRequest(NewPassword), RecoveryKey, "203.0.113.10");
        var customerResult = await service.RecoverAsync(
            BuildRequest(NewPassword, CustomerEmail),
            RecoveryKey,
            "203.0.113.10");

        adminResult.Status.Should().Be(AdminPasswordRecoveryStatus.TargetRejected);
        customerResult.Status.Should().Be(AdminPasswordRecoveryStatus.TargetRejected);
        (await db.AdminRecoveryKeyUsages.CountAsync()).Should().Be(0, "no key can be consumed without a target");

        var admin = await userManager.FindByEmailAsync(AdminEmail);
        (await userManager.CheckPasswordAsync(admin!, OldPassword)).Should().BeTrue(
            "the admin credential must never be touched when AdminSeed:Email is not configured");
    }

    [Fact]
    public async Task RecoverAsync_ShouldRejectCustomerAndUnknownTargets()
    {
        await using var provider = await CreateProviderAsync();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAdminPasswordRecoveryService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var customerResult = await service.RecoverAsync(
            BuildRequest(NewPassword, CustomerEmail),
            RecoveryKey,
            "203.0.113.10");
        var unknownResult = await service.RecoverAsync(
            BuildRequest(NewPassword, "unknown@urbeat.local"),
            RecoveryKey,
            "203.0.113.10");

        customerResult.Status.Should().Be(AdminPasswordRecoveryStatus.TargetRejected);
        unknownResult.Status.Should().Be(AdminPasswordRecoveryStatus.TargetRejected);
        (await db.AdminRecoveryKeyUsages.CountAsync()).Should().Be(0);

        var customer = await userManager.FindByEmailAsync(CustomerEmail);
        (await userManager.CheckPasswordAsync(customer!, "Customer123!")).Should().BeTrue(
            "the customer credential must never be touched by admin recovery");
    }

    [Fact]
    public async Task RecoverAsync_ShouldRejectAPasswordThatFailsIdentityValidation()
    {
        await using var provider = await CreateProviderAsync();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAdminPasswordRecoveryService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var result = await service.RecoverAsync(BuildRequest("weak"), RecoveryKey, null);

        result.Status.Should().Be(AdminPasswordRecoveryStatus.PasswordRejected);
        result.Error.Should().NotBeNullOrWhiteSpace();
        (await db.AdminRecoveryKeyUsages.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RecoverAsync_ShouldResetTheAdminPassword_RevokeSessions_AndBurnTheKey()
    {
        await using var provider = await CreateProviderAsync();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAdminPasswordRecoveryService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var admin = await userManager.FindByEmailAsync(AdminEmail);
        admin.Should().NotBeNull();
        var stampBefore = await userManager.GetSecurityStampAsync(admin!);
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = admin!.Id,
            Role = null,
            TokenHash = new string('a', 64),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(1)
        });
        await db.SaveChangesAsync();

        var result = await service.RecoverAsync(BuildRequest(NewPassword), RecoveryKey, "203.0.113.10");

        result.Status.Should().Be(AdminPasswordRecoveryStatus.Succeeded);
        (await userManager.CheckPasswordAsync(admin, NewPassword)).Should().BeTrue();
        (await userManager.CheckPasswordAsync(admin, OldPassword)).Should().BeFalse();

        var stampAfter = await userManager.GetSecurityStampAsync(admin);
        stampAfter.Should().NotBe(stampBefore, "a password reset must rotate the security stamp");

        var storedToken = await db.RefreshTokens.SingleAsync();
        storedToken.RevokedAtUtc.Should().NotBeNull("all admin sessions must be invalidated");

        var usage = await db.AdminRecoveryKeyUsages.SingleAsync();
        usage.AdminUserId.Should().Be(admin.Id);
        usage.KeyFingerprint.Should().HaveLength(64);
        usage.KeyFingerprint.Should().NotContain(RecoveryKey);
        usage.IpAddress.Should().Be("203.0.113.10");

        var audit = await db.AuditLogs.SingleAsync(x => x.Event == "AdminPasswordRecovered");
        audit.Description.Should().NotContain(NewPassword);
        audit.Description.Should().NotContain(RecoveryKey);
    }

    [Fact]
    public async Task RecoverAsync_ShouldRejectReuseOfAKeyThatAlreadySucceeded()
    {
        await using var provider = await CreateProviderAsync();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAdminPasswordRecoveryService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var first = await service.RecoverAsync(BuildRequest(NewPassword), RecoveryKey, null);
        var second = await service.RecoverAsync(BuildRequest(OtherPassword), RecoveryKey, null);

        first.Status.Should().Be(AdminPasswordRecoveryStatus.Succeeded);
        second.Status.Should().Be(AdminPasswordRecoveryStatus.KeyAlreadyUsed);

        var admin = await userManager.FindByEmailAsync(AdminEmail);
        (await userManager.CheckPasswordAsync(admin!, NewPassword)).Should().BeTrue(
            "the rejected second call must not overwrite the first recovery");
        (await userManager.CheckPasswordAsync(admin!, OtherPassword)).Should().BeFalse();
        (await db.AdminRecoveryKeyUsages.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task RecoverAsync_ShouldAllowOnlyOneSuccess_WhenCallsAreConcurrent()
    {
        await using var provider = await CreateProviderAsync();
        await using var firstScope = provider.CreateAsyncScope();
        await using var secondScope = provider.CreateAsyncScope();

        var firstService = firstScope.ServiceProvider.GetRequiredService<IAdminPasswordRecoveryService>();
        var secondService = secondScope.ServiceProvider.GetRequiredService<IAdminPasswordRecoveryService>();

        var results = await Task.WhenAll(
            firstService.RecoverAsync(BuildRequest(NewPassword), RecoveryKey, "203.0.113.10"),
            secondService.RecoverAsync(BuildRequest(OtherPassword), RecoveryKey, "203.0.113.11"));

        results.Count(x => x.Status == AdminPasswordRecoveryStatus.Succeeded).Should().Be(1);
        results.Count(x => x.Status == AdminPasswordRecoveryStatus.KeyAlreadyUsed).Should().Be(1);

        using var checkScope = provider.CreateScope();
        var db = checkScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = checkScope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        (await db.AdminRecoveryKeyUsages.CountAsync()).Should().Be(1);

        var admin = await userManager.FindByEmailAsync(AdminEmail);
        var winnerPassword = results[0].Status == AdminPasswordRecoveryStatus.Succeeded
            ? NewPassword
            : OtherPassword;
        (await userManager.CheckPasswordAsync(admin!, winnerPassword)).Should().BeTrue();
    }

    private static AdminRecoverPasswordRequestDto BuildRequest(
        string newPassword,
        string email = AdminEmail) => new()
    {
        Email = email,
        NewPassword = newPassword,
        ConfirmPassword = newPassword
    };

    private static async Task<ServiceProvider> CreateProviderAsync(
        bool enabled = true,
        string key = RecoveryKey,
        string? configuredAdminEmail = AdminEmail)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        // Keep the in-memory root alive across the seeding scope and the request scopes used by
        // the test; without an explicit root the store can be dropped between scopes.
        var databaseRoot = new InMemoryDatabaseRoot();
        var databaseName = $"urbeat-admin-recovery-{Guid.NewGuid()}";
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(databaseName, databaseRoot));
        services.AddIdentityCore<IdentityUser<Guid>>(options =>
            {
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredLength = 8;
                options.User.RequireUniqueEmail = true;
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        services.Configure<AdminRecoveryOptions>(options =>
        {
            options.Enabled = enabled;
            options.Key = key;
        });
        // AdminSeed:Email is configured separately from the seeded account so tests can prove the
        // service rejects every target when the configuration is missing.
        services.Configure<AdminSeedOptions>(options => options.Email = configuredAdminEmail!);
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IEfUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IAdminPasswordRecoveryService, AdminPasswordRecoveryService>();

        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
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

        var customer = new IdentityUser<Guid>
        {
            UserName = CustomerEmail,
            Email = CustomerEmail,
            EmailConfirmed = true
        };
        (await userManager.CreateAsync(customer, "Customer123!")).Succeeded.Should().BeTrue();
        (await userManager.AddToRoleAsync(customer, "Customer")).Succeeded.Should().BeTrue();

        return provider;
    }
}
