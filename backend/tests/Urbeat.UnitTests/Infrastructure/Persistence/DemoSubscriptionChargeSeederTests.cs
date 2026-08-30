using FluentAssertions;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace Urbeat.UnitTests.Infrastructure.Persistence;

public sealed class DemoSubscriptionChargeSeederTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly DemoSubscriptionChargeSeeder _sut;

    public DemoSubscriptionChargeSeederTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-demo-charge-{Guid.NewGuid()}")
            .Options;
        _db = new ApplicationDbContext(options);
        _sut = new DemoSubscriptionChargeSeeder(_db, Mock.Of<ILogger<DemoSubscriptionChargeSeeder>>());
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task SeedAsync_ShouldCreateTwoChargesForEachOfTwoOldestStores()
    {
        var oldest = await SeedSubscriptionAsync(DateTime.UtcNow.AddDays(-3));
        var middle = await SeedSubscriptionAsync(DateTime.UtcNow.AddDays(-2));
        var newest = await SeedSubscriptionAsync(DateTime.UtcNow.AddDays(-1));

        var created = await _sut.SeedAsync();

        created.Should().Be(4);

        var charges = await _db.SellerSubscriptionChargeHistories.ToListAsync();
        charges.Should().HaveCount(4);

        charges.Should().Contain(c => c.SellerUserId == oldest.SellerUserId);
        charges.Should().Contain(c => c.SellerUserId == middle.SellerUserId);
        charges.Should().NotContain(c => c.SellerUserId == newest.SellerUserId);

        foreach (var subscription in new[] { oldest, middle })
        {
            var storeCharges = charges.Where(c => c.SellerUserId == subscription.SellerUserId).ToList();
            storeCharges.Should().HaveCount(2);
            storeCharges.Should().ContainSingle(c => c.BillingStatus == SellerSubscriptionBillingStatus.Active && c.PaidAtUtc.HasValue);
            storeCharges.Should().ContainSingle(c => c.BillingStatus == SellerSubscriptionBillingStatus.Overdue && !c.PaidAtUtc.HasValue);
            storeCharges.Should().OnlyContain(c => c.GatewayChargeId.StartsWith($"DEMO-BASIC-{subscription.StoreId:N}-"));
            storeCharges.Should().ContainSingle(c => c.GatewayChargeId == $"DEMO-BASIC-{subscription.StoreId:N}-PAID");
            storeCharges.Should().ContainSingle(c => c.GatewayChargeId == $"DEMO-BASIC-{subscription.StoreId:N}-OVERDUE");
            storeCharges.Should().OnlyContain(c => c.Amount == subscription.PlanAmount);
            storeCharges.Should().OnlyContain(c => !string.IsNullOrWhiteSpace(c.ExternalReference));
            storeCharges.Should().OnlyContain(c => c.BillingPeriodStartUtc.HasValue && c.BillingPeriodEndUtc.HasValue);
        }
    }

    [Fact]
    public async Task SeedAsync_ShouldUseFixedPastPeriods_NotCurrentMonth()
    {
        await SeedSubscriptionAsync(DateTime.UtcNow.AddDays(-3));
        await SeedSubscriptionAsync(DateTime.UtcNow.AddDays(-2));

        var created = await _sut.SeedAsync();

        created.Should().Be(4);

        var charges = await _db.SellerSubscriptionChargeHistories.ToListAsync();
        charges.Should().OnlyContain(c => c.BillingPeriodStartUtc != null && c.BillingPeriodStartUtc.Value < DateTime.UtcNow.AddMonths(-1));
        charges.Should().OnlyContain(c => c.BillingPeriodStartUtc != null && c.BillingPeriodStartUtc.Value.Year < DateTime.UtcNow.Year);

        var paidPeriodStarts = charges
            .Where(c => c.BillingStatus == SellerSubscriptionBillingStatus.Active)
            .Select(c => c.BillingPeriodStartUtc!.Value)
            .Distinct()
            .ToList();
        paidPeriodStarts.Should().ContainSingle();
        paidPeriodStarts[0].Should().Be(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var overduePeriodStarts = charges
            .Where(c => c.BillingStatus == SellerSubscriptionBillingStatus.Overdue)
            .Select(c => c.BillingPeriodStartUtc!.Value)
            .Distinct()
            .ToList();
        overduePeriodStarts.Should().ContainSingle();
        overduePeriodStarts[0].Should().Be(new DateTime(2023, 12, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task SeedAsync_ShouldBeIdempotent()
    {
        await SeedSubscriptionAsync(DateTime.UtcNow.AddDays(-3));
        await SeedSubscriptionAsync(DateTime.UtcNow.AddDays(-2));

        var first = await _sut.SeedAsync();
        var second = await _sut.SeedAsync();

        first.Should().Be(4);
        second.Should().Be(0);

        var charges = await _db.SellerSubscriptionChargeHistories.ToListAsync();
        charges.Should().HaveCount(4);
    }

    [Fact]
    public async Task SeedAsync_WithSingleStore_ShouldSeedOnlyAvailable()
    {
        var subscription = await SeedSubscriptionAsync(DateTime.UtcNow.AddDays(-1));

        var created = await _sut.SeedAsync();

        created.Should().Be(2);

        var charges = await _db.SellerSubscriptionChargeHistories.ToListAsync();
        charges.Should().HaveCount(2);
        charges.Should().OnlyContain(c => c.SellerUserId == subscription.SellerUserId);
        charges.Should().ContainSingle(c => c.BillingStatus == SellerSubscriptionBillingStatus.Active);
        charges.Should().ContainSingle(c => c.BillingStatus == SellerSubscriptionBillingStatus.Overdue);
    }

    [Fact]
    public async Task SeedAsync_WithNoSubscriptions_ShouldCreateNothing()
    {
        var created = await _sut.SeedAsync();

        created.Should().Be(0);
        (await _db.SellerSubscriptionChargeHistories.CountAsync()).Should().Be(0);
    }

    private async Task<SellerSubscription> SeedSubscriptionAsync(DateTime createdAtUtc)
    {
        var subscription = new SellerSubscription
        {
            StoreId = Guid.NewGuid(),
            SellerUserId = Guid.NewGuid(),
            PlanId = Guid.NewGuid(),
            PlanName = BillingPlanSeeder.DefaultPlanName,
            PlanAmount = 49.90m,
            Status = SellerSubscriptionBillingStatus.Active,
            StartDateUtc = createdAtUtc,
            NextBillingDateUtc = createdAtUtc.AddMonths(1)
        };

        typeof(BaseEntity)
            .GetProperty(nameof(BaseEntity.CreatedAtUtc))!
            .SetValue(subscription, createdAtUtc);

        _db.SellerSubscriptions.Add(subscription);
        await _db.SaveChangesAsync();

        return subscription;
    }
}
