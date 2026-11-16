using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Urbeat.Application.Security;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Security;

namespace Urbeat.UnitTests.WebApi;

/// <summary>
/// The delegated audit stamp is centralized: business rows written under a live store context carry
/// the administrator as actor and the owner as subject, while seller sessions and out-of-request
/// writers are left untouched.
/// </summary>
public sealed class AdminStoreAuditStampTests
{
    private const string DatabaseName = "admin-store-audit-stamp-unit";
    private static readonly InMemoryDatabaseRoot DatabaseRoot = new();
    private static readonly MutableHttpContextAccessor Accessor = new();

    [Fact]
    public async Task DelegatedMutation_ShouldStampActorSubjectAndContext()
    {
        var adminUserId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var storeId = Guid.NewGuid();
        var contextId = Guid.NewGuid();
        Accessor.HttpContext = CreateContext(feature: new AdminStoreContextFeature
        {
            ContextId = contextId,
            AdminUserId = adminUserId,
            StoreId = storeId,
            StoreOwnerUserId = ownerUserId,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30)
        });

        await using var db = await CreateCleanDbContextAsync();
        db.AuditLogs.Add(new AuditLog
        {
            UserId = ownerUserId,
            Event = "StoreUpdated",
            Entity = nameof(Store),
            EntityId = storeId,
            Description = "Store updated."
        });

        await db.SaveChangesAsync();

        var audit = await db.AuditLogs.SingleAsync();
        audit.UserId.Should().Be(adminUserId);
        audit.ActorUserId.Should().Be(adminUserId);
        audit.SubjectUserId.Should().Be(ownerUserId);
        audit.AdminStoreContextId.Should().Be(contextId);
    }

    [Fact]
    public async Task SellerSession_ShouldKeepTheOriginalRow()
    {
        var sellerUserId = Guid.NewGuid();
        var storeId = Guid.NewGuid();
        Accessor.HttpContext = CreateContext(feature: null);

        await using var db = await CreateCleanDbContextAsync();
        db.AuditLogs.Add(new AuditLog
        {
            UserId = sellerUserId,
            Event = "StoreUpdated",
            Entity = nameof(Store),
            EntityId = storeId,
            Description = "Store updated."
        });

        await db.SaveChangesAsync();

        var audit = await db.AuditLogs.SingleAsync();
        audit.UserId.Should().Be(sellerUserId);
        audit.ActorUserId.Should().BeNull();
        audit.SubjectUserId.Should().BeNull();
        audit.AdminStoreContextId.Should().BeNull();
    }

    [Fact]
    public async Task OutOfRequestWrite_ShouldNotStampAnything()
    {
        var sellerUserId = Guid.NewGuid();
        Accessor.HttpContext = null;

        await using var db = await CreateCleanDbContextAsync();
        db.AuditLogs.Add(new AuditLog
        {
            UserId = sellerUserId,
            Event = "SellerRegistered",
            Entity = "IdentityUser",
            EntityId = sellerUserId,
            Description = "Registered."
        });

        await db.SaveChangesAsync();

        var audit = await db.AuditLogs.SingleAsync();
        audit.UserId.Should().Be(sellerUserId);
        audit.ActorUserId.Should().BeNull();
        audit.SubjectUserId.Should().BeNull();
        audit.AdminStoreContextId.Should().BeNull();
    }

    private static async Task<ApplicationDbContext> CreateCleanDbContextAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(DatabaseName, DatabaseRoot)
            .AddInterceptors(new AdminStoreAuditStampInterceptor(Accessor))
            .Options;

        var db = new ApplicationDbContext(options);
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private static DefaultHttpContext CreateContext(AdminStoreContextFeature? feature)
    {
        var context = new DefaultHttpContext();
        if (feature is not null)
        {
            context.Features.Set(feature);
        }

        return context;
    }

    private sealed class MutableHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }
}
