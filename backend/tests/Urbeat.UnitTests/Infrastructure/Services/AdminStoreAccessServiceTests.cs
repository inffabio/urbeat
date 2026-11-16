using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Urbeat.Application.DTOs.Admin;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Identity;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Security;
using Urbeat.Infrastructure.Services;

namespace Urbeat.UnitTests.Infrastructure.Services;

/// <summary>
/// Server-side invariants of the administrative store context: opaque single-store token, 30 minute
/// absolute lifetime, revocation/expiry handling, per-administrator limit and audit without secrets.
/// </summary>
public sealed class AdminStoreAccessServiceTests
{
    private const string AdminEmail = "admin@urbeat.local";
    private const string OtherAdminEmail = "outro.admin@urbeat.local";
    private const string SellerEmail = "lojista@urbeat.local";

    // A single InMemory database name/root (and therefore a single EF internal service provider)
    // keeps the suite from exhausting the provider cache; each test starts from a clean database.
    private const string DatabaseName = "admin-store-access-unit";
    private static readonly InMemoryDatabaseRoot DatabaseRoot = new();

    [Fact]
    public async Task CreateAsync_ShouldPersistOnlyTheHash_WithThirtyMinuteLifetime_AndAuditWithoutToken()
    {
        await using var ctx = await CreateContextAsync();
        var store = await SeedStoreAsync(ctx);
        var admin = await ctx.UserManager.FindByEmailAsync(AdminEmail);

        var result = await ctx.Service.CreateAsync(
            admin!.Id,
            store.Id,
            "Chamado de suporte #4821",
            "203.0.113.10",
            "Mozilla/5.0");

        result.Status.Should().Be(AdminStoreAccessStatus.Valid);
        result.Context.Should().NotBeNull();
        result.Context!.Token.Should().NotBeNullOrWhiteSpace();
        result.Context.StoreId.Should().Be(store.Id);
        result.Context.AdminUserId.Should().Be(admin.Id);
        result.Context.ExpiresAtUtc.Should().Be(ctx.Clock.GetUtcNow().UtcDateTime.AddMinutes(30));

        var persisted = await ctx.Db.AdminStoreAccessContexts.SingleAsync();
        persisted.TokenHash.Should().NotBe(result.Context.Token);
        persisted.TokenHash.Should().Be(ctx.TokenHasher.Hash(result.Context.Token));
        persisted.RevokedAtUtc.Should().BeNull();
        persisted.Reason.Should().Be("Chamado de suporte #4821");

        var audit = await ctx.Db.AuditLogs.SingleAsync(x => x.Event == "AdminStoreAccessGranted");
        audit.UserId.Should().Be(admin.Id);
        audit.ActorUserId.Should().Be(admin.Id);
        audit.SubjectUserId.Should().Be(store.OwnerUserId);
        audit.AdminStoreContextId.Should().Be(persisted.Id);
        audit.EntityId.Should().Be(store.Id);
        audit.IpAddress.Should().Be("203.0.113.10");
        audit.Description.Should().NotContain(result.Context.Token);
        audit.Description.Should().NotContain(persisted.TokenHash);
    }

    [Fact]
    public async Task CreateAsync_ShouldFailClosed_WhenStoreOrOwnerIsMissing()
    {
        await using var ctx = await CreateContextAsync();
        var admin = await ctx.UserManager.FindByEmailAsync(AdminEmail);

        var missingStore = await ctx.Service.CreateAsync(admin!.Id, Guid.NewGuid(), null, null, null);
        missingStore.Status.Should().Be(AdminStoreAccessStatus.StoreNotFound);
        missingStore.Context.Should().BeNull();

        var orphan = new Store
        {
            OwnerUserId = Guid.NewGuid(),
            Name = "Loja órfã",
            Slug = $"loja-orfa-{Guid.NewGuid():N}",
            PhoneNumber = "11999999999"
        };
        ctx.Db.Stores.Add(orphan);
        await ctx.Db.SaveChangesAsync();

        var orphanResult = await ctx.Service.CreateAsync(admin.Id, orphan.Id, null, null, null);
        orphanResult.Status.Should().Be(AdminStoreAccessStatus.StoreOwnerNotFound);

        (await ctx.Db.AdminStoreAccessContexts.CountAsync()).Should().Be(0, "no orphan context may be created");
    }

    [Fact]
    public async Task ValidateAsync_ShouldMarkExpiredOnce_AndRejectAfterwards()
    {
        await using var ctx = await CreateContextAsync();
        var store = await SeedStoreAsync(ctx);
        var admin = await ctx.UserManager.FindByEmailAsync(AdminEmail);
        var created = await ctx.Service.CreateAsync(admin!.Id, store.Id, null, null, null);

        ctx.Clock.Advance(TimeSpan.FromMinutes(31));

        var first = await ctx.Service.ValidateAsync(created.Context!.Token, admin.Id, null, true, null);
        first.Status.Should().Be(AdminStoreAccessStatus.Expired);
        var persisted = await ctx.Db.AdminStoreAccessContexts.SingleAsync();
        persisted.RevokedReason.Should().Be("Expired");
        persisted.RevokedAtUtc.Should().NotBeNull();

        var second = await ctx.Service.ValidateAsync(created.Context.Token, admin.Id, null, true, null);
        second.Status.Should().Be(AdminStoreAccessStatus.Expired);

        (await ctx.Db.AuditLogs.CountAsync(x => x.Event == "AdminStoreAccessExpired")).Should().Be(1);
    }

    [Fact]
    public async Task ValidateAsync_ShouldRejectUnknownTokens_ContextsOfAnotherAdmin_AndRevokedContexts()
    {
        await using var ctx = await CreateContextAsync();
        var store = await SeedStoreAsync(ctx);
        var admin = await ctx.UserManager.FindByEmailAsync(AdminEmail);
        var otherAdmin = await ctx.UserManager.FindByEmailAsync(OtherAdminEmail);
        var created = await ctx.Service.CreateAsync(admin!.Id, store.Id, null, null, null);

        (await ctx.Service.ValidateAsync("not-a-real-token", admin.Id, null, true, null))
            .Status.Should().Be(AdminStoreAccessStatus.Invalid);

        (await ctx.Service.ValidateAsync(created.Context!.Token, otherAdmin!.Id, null, true, null))
            .Status.Should().Be(AdminStoreAccessStatus.Invalid);

        (await ctx.Service.ValidateAsync(created.Context.Token, admin.Id, null, true, null))
            .Status.Should().Be(AdminStoreAccessStatus.Valid);

        (await ctx.Service.CloseAsync(created.Context.Token, admin.Id, store.Id, null))
            .Should().Be(AdminStoreAccessStatus.Valid);

        (await ctx.Service.ValidateAsync(created.Context.Token, admin.Id, null, true, null))
            .Status.Should().Be(AdminStoreAccessStatus.Invalid);
    }

    [Fact]
    public async Task ValidateAsync_ShouldRevokeAndReject_WhenTheActorLostTheAdminRole()
    {
        await using var ctx = await CreateContextAsync();
        var store = await SeedStoreAsync(ctx);
        var admin = await ctx.UserManager.FindByEmailAsync(AdminEmail);
        var created = await ctx.Service.CreateAsync(admin!.Id, store.Id, null, null, null);

        await ctx.UserManager.RemoveFromRoleAsync(admin!, "Admin");

        var validation = await ctx.Service.ValidateAsync(created.Context!.Token, admin!.Id, null, true, null);

        validation.Status.Should().Be(AdminStoreAccessStatus.AdminNotAllowed);
        var persisted = await ctx.Db.AdminStoreAccessContexts.SingleAsync();
        persisted.RevokedReason.Should().Be("AdminRoleRemoved");
        (await ctx.Db.AuditLogs.CountAsync(x => x.Event == "AdminStoreAccessRejected")).Should().Be(1);
    }

    [Fact]
    public async Task ValidateAsync_ShouldDenyAfterTheStoreIsDeleted_AndCascadeRemovesTheContext()
    {
        await using var ctx = await CreateContextAsync();
        var store = await SeedStoreAsync(ctx);
        var admin = await ctx.UserManager.FindByEmailAsync(AdminEmail);
        var created = await ctx.Service.CreateAsync(admin!.Id, store.Id, null, null, null);

        ctx.Db.Stores.Remove(store);
        await ctx.Db.SaveChangesAsync();

        // The FK is Cascade by contract: the delegated context row disappears with the store, so the
        // next request is denied as an unknown context (fail closed, never grants another store).
        ctx.Db.ChangeTracker.Clear();
        (await ctx.Db.AdminStoreAccessContexts.CountAsync()).Should().Be(0);

        var validation = await ctx.Service.ValidateAsync(created.Context!.Token, admin.Id, null, true, null);

        validation.Status.Should().Be(AdminStoreAccessStatus.Invalid);
    }

    [Fact]
    public async Task ValidateAsync_ShouldReject_AContextUsedAgainstAnotherStore()
    {
        await using var ctx = await CreateContextAsync();
        var store = await SeedStoreAsync(ctx);
        var admin = await ctx.UserManager.FindByEmailAsync(AdminEmail);
        var created = await ctx.Service.CreateAsync(admin!.Id, store.Id, null, null, null);

        var validation = await ctx.Service.ValidateAsync(
            created.Context!.Token,
            admin.Id,
            Guid.NewGuid(),
            true,
            null);

        validation.Status.Should().Be(AdminStoreAccessStatus.StoreMismatch);
    }

    [Fact]
    public async Task ValidateAsync_ShouldTouchLastSeen_AtMostOncePerInterval()
    {
        await using var ctx = await CreateContextAsync();
        var store = await SeedStoreAsync(ctx);
        var admin = await ctx.UserManager.FindByEmailAsync(AdminEmail);
        var created = await ctx.Service.CreateAsync(admin!.Id, store.Id, null, null, null);
        var firstTouch = (await ctx.Db.AdminStoreAccessContexts.SingleAsync()).LastSeenAtUtc;

        ctx.Clock.Advance(TimeSpan.FromSeconds(30));
        await ctx.Service.ValidateAsync(created.Context!.Token, admin.Id, null, true, null);
        ctx.Db.ChangeTracker.Clear();
        (await ctx.Db.AdminStoreAccessContexts.SingleAsync()).LastSeenAtUtc.Should().Be(firstTouch);

        ctx.Clock.Advance(TimeSpan.FromSeconds(40));
        await ctx.Service.ValidateAsync(created.Context.Token, admin.Id, null, true, null);
        ctx.Db.ChangeTracker.Clear();
        (await ctx.Db.AdminStoreAccessContexts.SingleAsync()).LastSeenAtUtc
            .Should().Be(ctx.Clock.GetUtcNow().UtcDateTime);
    }

    [Fact]
    public async Task CreateAsync_ShouldRevokeTheOldestContext_WhenTheLimitIsReached()
    {
        await using var ctx = await CreateContextAsync(options => options.MaxActiveContextsPerAdmin = 2);
        var store = await SeedStoreAsync(ctx);
        var admin = await ctx.UserManager.FindByEmailAsync(AdminEmail);

        var first = await ctx.Service.CreateAsync(admin!.Id, store.Id, null, null, null);
        ctx.Clock.Advance(TimeSpan.FromMinutes(1));
        var second = await ctx.Service.CreateAsync(admin.Id, store.Id, null, null, null);
        ctx.Clock.Advance(TimeSpan.FromMinutes(1));
        var third = await ctx.Service.CreateAsync(admin.Id, store.Id, null, null, null);

        var contexts = await ctx.Db.AdminStoreAccessContexts.ToListAsync();
        contexts.Should().HaveCount(3);
        contexts.Count(x => x.RevokedAtUtc == null).Should().Be(2);

        var oldest = contexts.Single(x => x.Id == first.Context!.ContextId);
        oldest.RevokedReason.Should().Be("Limit");

        (await ctx.Db.AuditLogs.CountAsync(x => x.Event == "AdminStoreAccessRevoked")).Should().Be(1);

        // The two most recent contexts remain usable.
        (await ctx.Service.ValidateAsync(second.Context!.Token, admin.Id, null, true, null))
            .Status.Should().Be(AdminStoreAccessStatus.Valid);
        (await ctx.Service.ValidateAsync(third.Context!.Token, admin.Id, null, true, null))
            .Status.Should().Be(AdminStoreAccessStatus.Valid);
    }

    [Fact]
    public async Task CloseAsync_ShouldBeIdempotent_AndRestrictedToThePresentedContext()
    {
        await using var ctx = await CreateContextAsync();
        var store = await SeedStoreAsync(ctx);
        var admin = await ctx.UserManager.FindByEmailAsync(AdminEmail);
        var otherAdmin = await ctx.UserManager.FindByEmailAsync(OtherAdminEmail);
        var created = await ctx.Service.CreateAsync(admin!.Id, store.Id, null, null, null);

        (await ctx.Service.CloseAsync(created.Context!.Token, admin.Id, Guid.NewGuid(), null))
            .Should().Be(AdminStoreAccessStatus.StoreMismatch);

        (await ctx.Service.CloseAsync(created.Context.Token, otherAdmin!.Id, store.Id, null))
            .Should().Be(AdminStoreAccessStatus.Invalid);

        (await ctx.Service.CloseAsync(created.Context.Token, admin.Id, store.Id, null))
            .Should().Be(AdminStoreAccessStatus.Valid);
        (await ctx.Service.CloseAsync(created.Context.Token, admin.Id, store.Id, null))
            .Should().Be(AdminStoreAccessStatus.AlreadyClosed);

        (await ctx.Db.AuditLogs.CountAsync(x => x.Event == "AdminStoreAccessRevoked")).Should().Be(1);
    }

    [Fact]
    public async Task CreateAsync_ShouldDeleteContextsBeyondTheRetentionWindow()
    {
        await using var ctx = await CreateContextAsync(options => options.ExpiredRetentionHours = 24);
        var store = await SeedStoreAsync(ctx);
        var admin = await ctx.UserManager.FindByEmailAsync(AdminEmail);

        var created = await ctx.Service.CreateAsync(admin!.Id, store.Id, null, null, null);
        await ctx.Service.CloseAsync(created.Context!.Token, admin.Id, store.Id, null);

        ctx.Clock.Advance(TimeSpan.FromHours(25));
        await ctx.Service.CreateAsync(admin.Id, store.Id, null, null, null);

        var contexts = await ctx.Db.AdminStoreAccessContexts.ToListAsync();
        contexts.Should().HaveCount(1);
        contexts[0].RevokedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task RevokeAllAsync_ShouldCloseOnlyTheActorsActiveContexts()
    {
        await using var ctx = await CreateContextAsync(options => options.MaxActiveContextsPerAdmin = 5);
        var store = await SeedStoreAsync(ctx);
        var admin = await ctx.UserManager.FindByEmailAsync(AdminEmail);
        var otherAdmin = await ctx.UserManager.FindByEmailAsync(OtherAdminEmail);

        await ctx.Service.CreateAsync(admin!.Id, store.Id, null, null, null);
        await ctx.Service.CreateAsync(admin.Id, store.Id, null, null, null);
        var other = await ctx.Service.CreateAsync(otherAdmin!.Id, store.Id, null, null, null);

        await ctx.Service.RevokeAllAsync(admin.Id, "Logout");

        var contexts = await ctx.Db.AdminStoreAccessContexts.ToListAsync();
        contexts.Where(row => row.AdminUserId == admin.Id)
            .Should().OnlyContain(row => row.RevokedReason == "Logout");
        contexts.Single(row => row.AdminUserId == otherAdmin.Id).RevokedAtUtc.Should().BeNull();

        (await ctx.Service.ValidateAsync(other.Context!.Token, otherAdmin.Id, null, true, null))
            .Status.Should().Be(AdminStoreAccessStatus.Valid);
    }

    private async Task<TestContext> CreateContextAsync(Action<AdminStoreAccessOptions>? configure = null)
    {
        var options = new AdminStoreAccessOptions();
        configure?.Invoke(options);

        var databaseName = $"admin-store-access-{Guid.NewGuid():N}";
        var databaseRoot = new InMemoryDatabaseRoot();
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddDbContext<ApplicationDbContext>(builder =>
            builder.UseInMemoryDatabase(DatabaseName, DatabaseRoot));
        services
            .AddIdentityCore<IdentityUser<Guid>>(identity =>
            {
                identity.Password.RequireDigit = false;
                identity.Password.RequireLowercase = false;
                identity.Password.RequireUppercase = false;
                identity.Password.RequireNonAlphanumeric = false;
                identity.Password.RequiredLength = 6;
                identity.User.RequireUniqueEmail = true;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<ApplicationDbContext>();
        services.AddSingleton<ITokenHasher, TokenHasher>();
        services.AddSingleton<TimeProvider>(clock);
        services.AddScoped(provider => new AdminStoreAccessService(
            provider.GetRequiredService<ApplicationDbContext>(),
            provider.GetRequiredService<ITokenHasher>(),
            clock,
            Options.Create(options),
            NullLogger<AdminStoreAccessService>.Instance));

        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        foreach (var roleName in new[] { "Admin", "Seller", "Customer" })
        {
            await roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
        }

        await CreateUserAsync(userManager, AdminEmail, "Admin");
        await CreateUserAsync(userManager, OtherAdminEmail, "Admin");
        await CreateUserAsync(userManager, SellerEmail, "Seller");

        return new TestContext(provider, scope, db, clock, userManager,
            scope.ServiceProvider.GetRequiredService<AdminStoreAccessService>());
    }

    private static async Task<Store> SeedStoreAsync(TestContext ctx)
    {
        var seller = await ctx.UserManager.FindByEmailAsync(SellerEmail);
        var store = new Store
        {
            OwnerUserId = seller!.Id,
            Name = "Pizza Hunter",
            Slug = "pizza-hunter",
            PhoneNumber = "11999999999"
        };

        ctx.Db.Stores.Add(store);
        await ctx.Db.SaveChangesAsync();
        return store;
    }

    private static async Task CreateUserAsync(
        UserManager<IdentityUser<Guid>> userManager,
        string email,
        string role)
    {
        var user = new IdentityUser<Guid>
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            EmailConfirmed = true
        };

        (await userManager.CreateAsync(user, "SenhaSegura123")).Succeeded.Should().BeTrue();
        (await userManager.AddToRoleAsync(user, role)).Succeeded.Should().BeTrue();
    }

    private sealed record TestContext(
        ServiceProvider Provider,
        IServiceScope Scope,
        ApplicationDbContext Db,
        MutableTimeProvider Clock,
        UserManager<IdentityUser<Guid>> UserManager,
        AdminStoreAccessService Service) : IAsyncDisposable
    {
        public ITokenHasher TokenHasher => Scope.ServiceProvider.GetRequiredService<ITokenHasher>();

        public async ValueTask DisposeAsync()
        {
            Scope.Dispose();
            await Provider.DisposeAsync();
        }
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public MutableTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }
}
