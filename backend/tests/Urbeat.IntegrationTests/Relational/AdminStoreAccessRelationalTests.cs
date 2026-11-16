using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;
using Urbeat.Application.DTOs.Admin;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Identity;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Persistence.ReadRepositories;
using Urbeat.Infrastructure.Security;
using Urbeat.Infrastructure.Services;
using Xunit.Abstractions;

namespace Urbeat.IntegrationTests.Relational;

/// <summary>
/// PostgreSQL-only invariants: unique token hashes, cascade removal of contexts with the store,
/// bounded active-context creation and the Npgsql-compatible listing projection.
/// </summary>
public sealed class AdminStoreAccessRelationalTests
{
    private readonly ITestOutputHelper _output;

    public AdminStoreAccessRelationalTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task TokenHash_ShouldBeUnique()
    {
        var container = await StartPostgresOrSkipAsync();
        if (container is null)
        {
            return;
        }

        try
        {
            await using var db = CreateDbContext(container.GetConnectionString());
            await db.Database.MigrateAsync();

            var (adminId, ownerId, storeId) = await SeedAsync(db);
            var hash = new TokenHasher().Hash("token-unico");

            db.AdminStoreAccessContexts.Add(BuildContext(adminId, storeId, hash));
            await db.SaveChangesAsync();

            db.AdminStoreAccessContexts.Add(BuildContext(adminId, storeId, hash));
            Func<Task> duplicate = async () => await db.SaveChangesAsync();

            await duplicate.Should().ThrowAsync<DbUpdateException>(
                "the unique index on TokenHash must reject duplicates");
        }
        finally
        {
            await container.DisposeAsync();
        }
    }

    [Fact]
    public async Task StoreDeletion_ShouldCascadeAndRemoveTheContexts()
    {
        var container = await StartPostgresOrSkipAsync();
        if (container is null)
        {
            return;
        }

        try
        {
            await using var db = CreateDbContext(container.GetConnectionString());
            await db.Database.MigrateAsync();

            var (adminId, ownerId, storeId) = await SeedAsync(db);
            db.AdminStoreAccessContexts.Add(BuildContext(adminId, storeId, new TokenHasher().Hash("token-cascata")));
            await db.SaveChangesAsync();

            var store = await db.Stores.SingleAsync(row => row.Id == storeId);
            db.Stores.Remove(store);
            await db.SaveChangesAsync();

            db.ChangeTracker.Clear();
            (await db.AdminStoreAccessContexts.CountAsync()).Should().Be(0);
            (await db.Users.CountAsync(user => user.Id == ownerId)).Should().Be(1,
                "the owner identity is not part of the store cascade");
        }
        finally
        {
            await container.DisposeAsync();
        }
    }

    [Fact]
    public async Task ConcurrentCreation_ShouldKeepAtMostTheConfiguredLimitActive()
    {
        var container = await StartPostgresOrSkipAsync();
        if (container is null)
        {
            return;
        }

        try
        {
            var connectionString = container.GetConnectionString();
            await using (var migrate = CreateDbContext(connectionString))
            {
                await migrate.Database.MigrateAsync();
                await SeedAsync(migrate);
            }

            await using var provider = CreateProvider(connectionString);
            Guid adminId;
            Guid storeId;
            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                adminId = await db.Users.Where(user => user.Email == "admin.access@urbeat.local").Select(user => user.Id).SingleAsync();
                storeId = await db.Stores.Select(store => store.Id).SingleAsync();
            }

            var creates = Enumerable.Range(0, 4).Select(async _ =>
            {
                await using var scope = provider.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<IAdminStoreAccessService>();
                return await service.CreateAsync(adminId, storeId, null, "203.0.113.10", "tests");
            });

            var results = await Task.WhenAll(creates);
            results.Should().OnlyContain(result => result.Context != null);

            await using var checkScope = provider.CreateAsyncScope();
            var check = checkScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var rows = await check.AdminStoreAccessContexts.ToListAsync();
            rows.Count(row => row.RevokedAtUtc == null).Should().Be(2);
            rows.Should().OnlyContain(row => row.ExpiresAtUtc.Kind == DateTimeKind.Utc);
        }
        finally
        {
            await container.DisposeAsync();
        }
    }

    [Fact]
    public async Task Listing_ShouldProjectOnNpgsql_WithSearchAndFilters()
    {
        var container = await StartPostgresOrSkipAsync();
        if (container is null)
        {
            return;
        }

        try
        {
            await using var db = CreateDbContext(container.GetConnectionString());
            await db.Database.MigrateAsync();

            var (_, ownerId, storeId) = await SeedAsync(db);
            var repository = new AdminStoreReadRepository(db);

            var page = await repository.ListAsync(
                new AdminStoresQueryDto { Search = "maria", Status = "onboarding" },
                100);

            page.TotalItems.Should().Be(1);
            page.Items.Single().Id.Should().Be(storeId);
            page.Items.Single().Owner.Name.Should().Be("Maria Souza");
            page.Items.Single().Status.Should().Be("Onboarding");
            page.Items.Single().Subscription.Status.Should().Be("None");

            (await repository.ListAsync(new AdminStoresQueryDto { Subscription = "none" }, 100))
                .Items.Should().ContainSingle(item => item.Id == storeId);
            (await repository.ListAsync(new AdminStoresQueryDto { Publication = "published" }, 100))
                .Items.Should().BeEmpty();
        }
        finally
        {
            await container.DisposeAsync();
        }
    }

    private static ApplicationDbContext CreateDbContext(string connectionString)
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .Options);

    private static ServiceProvider CreateProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connectionString));
        services.AddSingleton<ITokenHasher, TokenHasher>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IAdminStoreReadRepository, AdminStoreReadRepository>();
        services.AddScoped(provider => new AdminStoreAccessService(
            provider.GetRequiredService<ApplicationDbContext>(),
            provider.GetRequiredService<ITokenHasher>(),
            TimeProvider.System,
            Options.Create(new AdminStoreAccessOptions
            {
                ContextLifetimeMinutes = 30,
                MaxActiveContextsPerAdmin = 2,
                ExpiredRetentionHours = 24,
                TouchIntervalSeconds = 60
            }),
            NullLogger<AdminStoreAccessService>.Instance));
        services.AddScoped<IAdminStoreAccessService>(provider =>
            provider.GetRequiredService<AdminStoreAccessService>());

        return services.BuildServiceProvider();
    }

    private static AdminStoreAccessContext BuildContext(Guid adminId, Guid storeId, string hash)
        => new()
        {
            AdminUserId = adminId,
            StoreId = storeId,
            TokenHash = hash,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30)
        };

    private static async Task<(Guid AdminId, Guid OwnerId, Guid StoreId)> SeedAsync(ApplicationDbContext db)
    {
        var adminRole = new IdentityRole<Guid>
        {
            Id = Guid.CreateVersion7(),
            Name = "Admin",
            NormalizedName = "ADMIN",
            ConcurrencyStamp = Guid.NewGuid().ToString()
        };
        var sellerRole = new IdentityRole<Guid>
        {
            Id = Guid.CreateVersion7(),
            Name = "Seller",
            NormalizedName = "SELLER",
            ConcurrencyStamp = Guid.NewGuid().ToString()
        };
        db.Roles.AddRange(adminRole, sellerRole);

        var admin = new IdentityUser<Guid>
        {
            Id = Guid.CreateVersion7(),
            UserName = "admin.access@urbeat.local",
            NormalizedUserName = "ADMIN.ACCESS@URBEAT.LOCAL",
            Email = "admin.access@urbeat.local",
            NormalizedEmail = "ADMIN.ACCESS@URBEAT.LOCAL",
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString()
        };
        var owner = new IdentityUser<Guid>
        {
            Id = Guid.CreateVersion7(),
            UserName = "maria.souza@urbeat.local",
            NormalizedUserName = "MARIA.SOUZA@URBEAT.LOCAL",
            Email = "maria.souza@urbeat.local",
            NormalizedEmail = "MARIA.SOUZA@URBEAT.LOCAL",
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString()
        };
        db.Users.AddRange(admin, owner);
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = admin.Id, RoleId = adminRole.Id });
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = owner.Id, RoleId = sellerRole.Id });
        db.UserClaims.Add(new IdentityUserClaim<Guid>
        {
            UserId = owner.Id,
            ClaimType = "FullName",
            ClaimValue = "Maria Souza"
        });

        var store = new Store
        {
            OwnerUserId = owner.Id,
            Name = "Pizza Hunter",
            Slug = "pizza-hunter",
            PhoneNumber = "11999999999",
            IsPublished = false
        };
        db.Stores.Add(store);

        await db.SaveChangesAsync();
        return (admin.Id, owner.Id, store.Id);
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
