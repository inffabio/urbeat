using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Urbeat.Application.DTOs.Admin;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Persistence.ReadRepositories;

namespace Urbeat.UnitTests.Infrastructure.Persistence;

/// <summary>
/// Contract of the administrative store listing: deterministic pagination/ordering, case-insensitive
/// search across name/slug/e-mail/owner name, independent filters and no operational PII.
/// </summary>
public sealed class AdminStoreReadRepositoryTests
{
    private static readonly InMemoryDatabaseRoot DatabaseRoot = new();
    [Fact]
    public async Task ListAsync_ShouldPageAndOrderByName_WithClampedPageSize()
    {
        await using var db = await CreateDbContextAsync();

        var page = await new AdminStoreReadRepository(db).ListAsync(new AdminStoresQueryDto(), maxPageSize: 100);

        page.Page.Should().Be(1);
        page.PageSize.Should().Be(20);
        page.TotalItems.Should().Be(3);
        page.Items.Select(item => item.Name).Should().ContainInOrder("Cantina da Praça", "Pizza Hunter", "Sushi Zen");

        var clamped = await new AdminStoreReadRepository(db).ListAsync(
            new AdminStoresQueryDto { Page = 0, PageSize = 500 },
            maxPageSize: 100);

        clamped.Page.Should().Be(1);
        clamped.PageSize.Should().Be(100);
    }

    [Fact]
    public async Task ListAsync_ShouldSearchByNameSlugOwnerEmailAndOwnerName()
    {
        await using var db = await CreateDbContextAsync();
        var repository = new AdminStoreReadRepository(db);

        (await repository.ListAsync(new AdminStoresQueryDto { Search = "pizza" }, 100))
            .Items.Should().ContainSingle(item => item.Slug == "pizza-hunter");

        (await repository.ListAsync(new AdminStoresQueryDto { Search = "CANTINA" }, 100))
            .Items.Should().ContainSingle(item => item.Slug == "cantina-da-praca");

        (await repository.ListAsync(new AdminStoresQueryDto { Search = "maria.souza" }, 100))
            .Items.Should().ContainSingle(item => item.Slug == "pizza-hunter");

        (await repository.ListAsync(new AdminStoresQueryDto { Search = "joão lima" }, 100))
            .Items.Should().ContainSingle(item => item.Slug == "cantina-da-praca");
    }

    [Fact]
    public async Task ListAsync_ShouldApplyTheIndependentFilters()
    {
        await using var db = await CreateDbContextAsync();
        var repository = new AdminStoreReadRepository(db);

        (await repository.ListAsync(new AdminStoresQueryDto { Status = "onboarding" }, 100))
            .Items.Should().ContainSingle(item => item.Slug == "cantina-da-praca");
        (await repository.ListAsync(new AdminStoresQueryDto { Status = "suspended" }, 100))
            .Items.Should().ContainSingle(item => item.Slug == "sushi-zen");

        (await repository.ListAsync(new AdminStoresQueryDto { Publication = "draft" }, 100))
            .Items.Should().ContainSingle(item => item.Slug == "cantina-da-praca");
        (await repository.ListAsync(new AdminStoresQueryDto { OpenState = "closed" }, 100))
            .Items.Should().ContainSingle(item => item.Slug == "cantina-da-praca");
        (await repository.ListAsync(new AdminStoresQueryDto { Subscription = "none" }, 100))
            .Items.Should().ContainSingle(item => item.Slug == "cantina-da-praca");
        (await repository.ListAsync(new AdminStoresQueryDto { Subscription = "overdue" }, 100))
            .Items.Should().ContainSingle(item => item.Slug == "pizza-hunter");
    }

    [Fact]
    public async Task ListAsync_ShouldDeriveStatusAndExposeOnlySafeFields()
    {
        await using var db = await CreateDbContextAsync();
        var page = await new AdminStoreReadRepository(db).ListAsync(new AdminStoresQueryDto(), 100);

        var pizza = page.Items.Single(item => item.Slug == "pizza-hunter");
        pizza.Status.Should().Be("Active");
        pizza.Owner.Email.Should().Be("maria.souza@exemplo.com");
        pizza.Owner.Name.Should().Be("Maria Souza");
        pizza.Subscription.Status.Should().Be("Overdue");
        pizza.Subscription.PlanName.Should().Be("Pro");

        var cantina = page.Items.Single(item => item.Slug == "cantina-da-praca");
        cantina.Status.Should().Be("Onboarding");
        cantina.LogoUrl.Should().BeNull();
        cantina.Subscription.Status.Should().Be("None");
        cantina.Subscription.PlanName.Should().BeNull();

        var sushi = page.Items.Single(item => item.Slug == "sushi-zen");
        sushi.Status.Should().Be("Suspended");
        sushi.IsSubscriptionBlocked.Should().BeTrue();

        // No document, Pix key, phone number or address may leave the listing.
        var json = JsonSerializer.Serialize(page).ToLowerInvariant();
        json.Should().NotContain("document");
        json.Should().NotContain("pixkey");
        json.Should().NotContain("phonenumber");
        json.Should().NotContain("street");
    }

    [Fact]
    public async Task ListAsync_ShouldHideK6Fixtures_ByStrictSlugPattern()
    {
        await using var db = await CreateDbContextAsync();
        var owner = await AddOwnerAsync(db, "fixtures@exemplo.com", "Fixtures Owner");

        var hidden = new[]
        {
            // Observed fixture: slug load-<timestamp>-<uuid>, name "Store anchor".
            NewStore(owner.Id, "Store anchor", "load-20261109204926-f5594855"),
            // Current wrapper RUN_ID is already "load-<timestamp>-<uuid>" and k6 appends its label.
            NewStore(owner.Id, "Store s3", "load-load-20261109204926-f5594855-s3"),
            // Matching is case-insensitive, as slugs are stored lowercase by validation.
            NewStore(owner.Id, "Store anchor", "LOAD-20261109204926-F5594855-ANCHOR")
        };
        var visible = new[]
        {
            // A load-* slug without the timestamp/UUID core is a legitimate store.
            NewStore(owner.Id, "Padaria do Bairro", "load-padaria-20261118"),
            // The fixture name alone never hides a store.
            NewStore(owner.Id, "Store anchor", "store-anchor"),
            // A "load test" name alone is not enough either.
            NewStore(owner.Id, "Cantina Load Test", "cantina-load-test"),
            // Legacy load-* slugs outside the strict timestamp/UUID shape stay visible.
            NewStore(owner.Id, "Loja Antiga", "load-20261118-a-s1"),
            // The core followed by a non-k6 label is outside the script-generated shape.
            NewStore(owner.Id, "Pizzaria do Zé", "load-20261109204926-f5594855-pizzaria")
        };

        db.Stores.AddRange(hidden);
        db.Stores.AddRange(visible);
        await db.SaveChangesAsync();

        var repository = new AdminStoreReadRepository(db);
        var page = await repository.ListAsync(new AdminStoresQueryDto(), 100);

        page.TotalItems.Should().Be(8);
        page.TotalPages.Should().Be(1);
        page.Items.Select(item => item.Slug).Should().NotContain(hidden.Select(store => store.Slug));
        page.Items.Select(item => item.Slug).Should().Contain(visible.Select(store => store.Slug));

        // Search must not bypass the hidden fixtures.
        var searched = await repository.ListAsync(new AdminStoresQueryDto { Search = "load" }, 100);
        searched.TotalItems.Should().Be(4);
        searched.Items.Select(item => item.Slug).Should().BeEquivalentTo(
            "load-padaria-20261118",
            "cantina-load-test",
            "load-20261118-a-s1",
            "load-20261109204926-f5594855-pizzaria");

        // The rows are hidden, never deleted.
        (await db.Stores.CountAsync()).Should().Be(11);
    }

    [Fact]
    public async Task ListAsync_ShouldKeepTotalsAndPagesConsistentAfterHidingK6Fixtures()
    {
        await using var db = await CreateDbContextAsync();
        var owner = await AddOwnerAsync(db, "fixtures.paging@exemplo.com", "Fixtures Paging");

        db.Stores.AddRange(
            NewStore(owner.Id, "Store anchor", "load-20261109204926-f5594855"),
            NewStore(owner.Id, "Store s1", "load-20261109204926-f5594856"),
            NewStore(owner.Id, "Store s2", "load-20261109204926-f5594857"));
        await db.SaveChangesAsync();

        var repository = new AdminStoreReadRepository(db);

        var firstPage = await repository.ListAsync(new AdminStoresQueryDto { Page = 1, PageSize = 2 }, 100);
        firstPage.TotalItems.Should().Be(3);
        firstPage.TotalPages.Should().Be(2);
        firstPage.Items.Should().HaveCount(2);

        var secondPage = await repository.ListAsync(new AdminStoresQueryDto { Page = 2, PageSize = 2 }, 100);
        secondPage.TotalItems.Should().Be(3);
        secondPage.TotalPages.Should().Be(2);
        secondPage.Items.Should().ContainSingle(item => item.Slug == "sushi-zen");
        secondPage.Items.Select(item => item.Slug)
            .Should().NotIntersectWith(firstPage.Items.Select(item => item.Slug));
    }

    [Fact]
    public void LoadTestSlugPattern_ShouldBeTranslatedByNpgsql()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=urbeat;Username=urbeat;Password=urbeat")
            .Options;
        using var db = new ApplicationDbContext(options);

        var sql = db.Stores
            .Where(store => Regex.IsMatch(store.Slug.ToLower(), AdminStoreReadRepository.LoadTestSlugPattern))
            .ToQueryString();

        // The administrative fixture filter must run server-side on PostgreSQL, not client-side.
        sql.Should().Contain("~");
        sql.Should().Contain(AdminStoreReadRepository.LoadTestSlugPattern);
    }

    private static Store NewStore(Guid ownerUserId, string name, string slug) => new()
    {
        OwnerUserId = ownerUserId,
        Name = name,
        Slug = slug,
        PhoneNumber = "11955555555"
    };

    private static async Task<ApplicationDbContext> CreateDbContextAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("admin-store-read-unit", DatabaseRoot)
            .Options;
        var db = new ApplicationDbContext(options);
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        var maria = await AddOwnerAsync(db, "maria.souza@exemplo.com", "Maria Souza");
        var joao = await AddOwnerAsync(db, "cantina@exemplo.com", "João Lima");
        var ana = await AddOwnerAsync(db, "sushi@exemplo.com", "Ana Costa");

        var pizza = new Store
        {
            OwnerUserId = maria.Id,
            Name = "Pizza Hunter",
            Slug = "pizza-hunter",
            PhoneNumber = "11999999999",
            Document = "12345678901",
            PixKey = "pix@exemplo.com",
            IsPublished = true,
            IsOpen = true,
            LogoUrl = "https://cdn.example/logo.png"
        };
        var cantina = new Store
        {
            OwnerUserId = joao.Id,
            Name = "Cantina da Praça",
            Slug = "cantina-da-praca",
            PhoneNumber = "11888888888",
            IsPublished = false,
            IsOpen = false
        };
        var sushi = new Store
        {
            OwnerUserId = ana.Id,
            Name = "Sushi Zen",
            Slug = "sushi-zen",
            PhoneNumber = "11777777777",
            IsPublished = true,
            IsOpen = true,
            IsSubscriptionBlocked = true
        };

        db.Stores.AddRange(pizza, cantina, sushi);
        db.SellerSubscriptionStatuses.Add(new SellerSubscriptionStatus
        {
            SellerUserId = maria.Id,
            BillingStatus = SellerSubscriptionBillingStatus.Overdue,
            NextDueDateUtc = new DateTime(2026, 12, 5, 0, 0, 0, DateTimeKind.Utc)
        });
        db.SellerSubscriptionStatuses.Add(new SellerSubscriptionStatus
        {
            SellerUserId = ana.Id,
            BillingStatus = SellerSubscriptionBillingStatus.Blocked,
            NextDueDateUtc = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        db.SellerSubscriptions.Add(new SellerSubscription
        {
            StoreId = pizza.Id,
            SellerUserId = maria.Id,
            PlanName = "Pro",
            PlanAmount = 99m,
            Status = SellerSubscriptionBillingStatus.Overdue,
            StartDateUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            NextBillingDateUtc = new DateTime(2026, 12, 5, 0, 0, 0, DateTimeKind.Utc)
        });

        await db.SaveChangesAsync();
        return db;
    }

    private static async Task<IdentityUser<Guid>> AddOwnerAsync(
        ApplicationDbContext db,
        string email,
        string fullName)
    {
        var user = new IdentityUser<Guid>
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            EmailConfirmed = true
        };

        db.Users.Add(user);
        db.UserClaims.Add(new IdentityUserClaim<Guid>
        {
            UserId = user.Id,
            ClaimType = "FullName",
            ClaimValue = fullName
        });

        await db.SaveChangesAsync();
        return user;
    }
}
