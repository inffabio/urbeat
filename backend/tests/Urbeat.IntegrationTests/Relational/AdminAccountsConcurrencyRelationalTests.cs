using FluentAssertions;
using Hangfire;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Testcontainers.PostgreSql;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Domain.Repositories;
using Urbeat.Infrastructure.Identity;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Persistence.ReadRepositories;
using Urbeat.Infrastructure.Persistence.Repositories;
using Urbeat.Infrastructure.Persistence.UnitOfWork;
using Xunit.Abstractions;

namespace Urbeat.IntegrationTests.Relational;

/// <summary>
/// PostgreSQL enforces the invariants InMemory cannot: one active link per target/purpose (partial
/// unique index), exactly one winner when two administrators remove each other, and exactly one
/// winner when the same invitation is accepted concurrently.
/// </summary>
public sealed class AdminAccountsConcurrencyRelationalTests
{
    private const string SigningKey = "urbeat-relational-admin-account-signing-key-32chars";
    private const string AdminPassword = "Admin12345";

    private readonly ITestOutputHelper _output;

    public AdminAccountsConcurrencyRelationalTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task PartialUniqueIndex_ShouldRejectASecondActiveInvitationForTheSameTargetAndPurpose()
    {
        var container = await StartPostgresOrSkipAsync();
        if (container is null)
        {
            return;
        }

        try
        {
            var connectionString = container.GetConnectionString();
            await using var db = CreateDbContext(connectionString);
            await db.Database.MigrateAsync();

            var userId = await SeedAdministratorAsync(db, "unico@urbeat.local", confirmed: false);

            db.AdminInvitations.Add(BuildInvitation(userId, 'A'));
            await db.SaveChangesAsync();

            db.AdminInvitations.Add(BuildInvitation(userId, 'B'));

            Func<Task> duplicate = async () => await db.SaveChangesAsync();
            await duplicate.Should().ThrowAsync<DbUpdateException>(
                "the filtered unique index must reject two active links");

            db.ChangeTracker.Clear();

            // Once the first link is consumed, a new active link is allowed again.
            var first = await db.AdminInvitations.SingleAsync();
            first.ConsumedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();

            db.AdminInvitations.Add(BuildInvitation(userId, 'C'));
            await db.SaveChangesAsync();
            (await db.AdminInvitations.CountAsync()).Should().Be(2);
        }
        finally
        {
            await container.DisposeAsync();
        }
    }

    [Fact]
    public async Task ConcurrentRemovalsOfTheLastTwoAdministrators_ShouldAllowExactlyOne()
    {
        var container = await StartPostgresOrSkipAsync();
        if (container is null)
        {
            return;
        }

        try
        {
            await using var provider = await CreateProviderAsync(container);
            var firstId = await GetUserIdAsync(provider, "primeiro@urbeat.local");
            var secondId = await GetUserIdAsync(provider, "segundo@urbeat.local");

            await using var firstScope = provider.CreateAsyncScope();
            await using var secondScope = provider.CreateAsyncScope();
            var firstService = firstScope.ServiceProvider.GetRequiredService<IAdminAccountService>();
            var secondService = secondScope.ServiceProvider.GetRequiredService<IAdminAccountService>();

            var results = await Task.WhenAll(
                firstService.RemoveAsync(secondId, "concorrente", firstId, "203.0.113.10"),
                secondService.RemoveAsync(firstId, "concorrente", secondId, "203.0.113.11"));

            results.Count(result => result.Succeeded).Should().Be(1);
            results.Count(result => result.Status == AdminAccountOperationStatus.LastActiveAdmin).Should().Be(1);

            await using var checkScope = provider.CreateAsyncScope();
            var db = checkScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var adminRoleId = await db.Roles
                .Where(role => role.Name == "Admin")
                .Select(role => role.Id)
                .SingleAsync();
            var remainingAdmins = await db.UserRoles.CountAsync(role => role.RoleId == adminRoleId);
            remainingAdmins.Should().Be(1, "exactly one administrator must remain");
        }
        finally
        {
            await container.DisposeAsync();
        }
    }

    [Fact]
    public async Task ConcurrentAcceptsOfTheSameInvitation_ShouldAllowExactlyOne()
    {
        var container = await StartPostgresOrSkipAsync();
        if (container is null)
        {
            return;
        }

        try
        {
            await using var provider = await CreateProviderAsync(container);
            var targetId = await GetUserIdAsync(provider, "convite@urbeat.local");

            string token;
            using (var setupScope = provider.CreateScope())
            {
                var invitations = setupScope.ServiceProvider.GetRequiredService<IAdminInvitationService>();
                var invitation = await invitations.CreateAsync(
                    targetId,
                    "convite@urbeat.local",
                    AdminInvitationPurpose.AccountInvitation,
                    null);
                token = invitations.CreateToken(invitation.Id, AdminInvitationPurpose.AccountInvitation);
            }

            await using var firstScope = provider.CreateAsyncScope();
            await using var secondScope = provider.CreateAsyncScope();
            var firstService = firstScope.ServiceProvider.GetRequiredService<IAdminAccountService>();
            var secondService = secondScope.ServiceProvider.GetRequiredService<IAdminAccountService>();

            var results = await Task.WhenAll(
                firstService.AcceptInvitationAsync(token, "NovaSenha123"),
                secondService.AcceptInvitationAsync(token, "OutraSenha123"));

            results.Count(result => result.Succeeded).Should().Be(1);
            results.Count(result => !result.Succeeded).Should().Be(1);

            await using var checkScope = provider.CreateAsyncScope();
            var db = checkScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userManager = checkScope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
            var invitationRow = await db.AdminInvitations.SingleAsync();
            invitationRow.ConsumedAtUtc.Should().NotBeNull();

            var user = await userManager.FindByIdAsync(targetId.ToString());
            user!.EmailConfirmed.Should().BeTrue();
            var winnerPassword = results[0].Succeeded ? "NovaSenha123" : "OutraSenha123";
            (await userManager.CheckPasswordAsync(user, winnerPassword)).Should().BeTrue();
        }
        finally
        {
            await container.DisposeAsync();
        }
    }

    private static AdminInvitation BuildInvitation(Guid userId, char hashCharacter) => new()
    {
        UserId = userId,
        Email = "unico@urbeat.local",
        Purpose = AdminInvitationPurpose.AccountInvitation,
        TokenHash = new string(hashCharacter, 64),
        ExpiresAtUtc = DateTime.UtcNow.AddHours(24)
    };

    private static ApplicationDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new ApplicationDbContext(options);
    }

    private static async Task<Guid> SeedAdministratorAsync(
        ApplicationDbContext db,
        string email,
        bool confirmed)
    {
        var roleId = Guid.CreateVersion7();
        db.Roles.Add(new IdentityRole<Guid>
        {
            Id = roleId,
            Name = "Admin",
            NormalizedName = "ADMIN",
            ConcurrencyStamp = Guid.NewGuid().ToString()
        });

        var user = new IdentityUser<Guid>
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = confirmed,
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString()
        };
        db.Users.Add(user);
        db.UserRoles.Add(new Microsoft.AspNetCore.Identity.IdentityUserRole<Guid>
        {
            UserId = user.Id,
            RoleId = roleId
        });
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static async Task<ServiceProvider> CreateProviderAsync(PostgreSqlContainer container)
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
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ITokenHasher, TokenHasher>();
        services.Configure<RolePromotionChallengeOptions>(options => options.SigningKey = SigningKey);
        services.Configure<AdminAccountOptions>(_ => { });
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IEfUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IAdminInvitationService, AdminInvitationService>();
        services.AddScoped<IAdminAccountReadRepository, AdminAccountReadRepository>();
        services.AddScoped<IAdminAccountService, AdminAccountService>();
        services.AddSingleton(Mock.Of<IBackgroundJobClient>());

        var provider = services.BuildServiceProvider();

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.MigrateAsync();

            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            if (!await roleManager.RoleExistsAsync("Admin"))
            {
                await roleManager.CreateAsync(new IdentityRole<Guid>("Admin"));
            }

            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
            await CreateAdminAsync(userManager, "primeiro@urbeat.local", confirmed: true);
            await CreateAdminAsync(userManager, "segundo@urbeat.local", confirmed: true);
            await CreateAdminAsync(userManager, "convite@urbeat.local", confirmed: false);
        }

        return provider;
    }

    private static async Task CreateAdminAsync(
        UserManager<IdentityUser<Guid>> userManager,
        string email,
        bool confirmed)
    {
        var user = new IdentityUser<Guid>
        {
            UserName = email,
            Email = email,
            EmailConfirmed = confirmed
        };

        IdentityResult result = confirmed
            ? await userManager.CreateAsync(user, AdminPassword)
            : await userManager.CreateAsync(user);
        result.Succeeded.Should().BeTrue();
        (await userManager.AddToRoleAsync(user, "Admin")).Succeeded.Should().BeTrue();
    }

    private static async Task<Guid> GetUserIdAsync(ServiceProvider provider, string email)
    {
        using var scope = provider.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var user = await userManager.FindByEmailAsync(email);
        user.Should().NotBeNull();
        return user!.Id;
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
