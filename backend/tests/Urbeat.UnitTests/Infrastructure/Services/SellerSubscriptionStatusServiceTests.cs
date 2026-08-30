using FluentAssertions;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Persistence.UnitOfWork;
using Urbeat.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Urbeat.UnitTests.Infrastructure.Services;

public sealed class SellerSubscriptionStatusServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly SellerSubscriptionStatusService _sut;

    public SellerSubscriptionStatusServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-subscription-status-{Guid.NewGuid()}")
            .Options;
        _db = new ApplicationDbContext(options);

        _sut = new SellerSubscriptionStatusService(
            _db,
            new EfUnitOfWork(_db),
            Mock.Of<IAsaasSubscriptionAdapter>());
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task ListMyChargeHistoryAsync_ShouldOrderByDueDateDescending()
    {
        var sellerUserId = Guid.NewGuid();
        await SeedCharge(sellerUserId, "charge-jul", new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc));
        await SeedCharge(sellerUserId, "charge-jun", new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc));
        await SeedCharge(sellerUserId, "charge-aug", new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc));

        var result = await _sut.ListMyChargeHistoryAsync(sellerUserId);

        result.Select(x => x.GatewayChargeId).Should().Equal("charge-aug", "charge-jul", "charge-jun");
    }

    [Fact]
    public async Task ListMyChargeHistoryAsync_ShouldKeepSellerIsolation()
    {
        var sellerA = Guid.NewGuid();
        var sellerB = Guid.NewGuid();
        await SeedCharge(sellerA, "charge-a", new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc));
        await SeedCharge(sellerB, "charge-b", new DateTime(2026, 8, 11, 0, 0, 0, DateTimeKind.Utc));

        var result = await _sut.ListMyChargeHistoryAsync(sellerA);

        result.Should().ContainSingle();
        result[0].GatewayChargeId.Should().Be("charge-a");
    }

    [Fact]
    public async Task ListMyChargeHistoryAsync_ShouldReturnBillingPeriod()
    {
        var sellerUserId = Guid.NewGuid();
        var start = new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc);
        await SeedCharge(sellerUserId, "charge-period", start, billingPeriodStartUtc: start, billingPeriodEndUtc: end);

        var result = await _sut.ListMyChargeHistoryAsync(sellerUserId);

        result.Should().ContainSingle();
        result[0].BillingPeriodStartUtc.Should().Be(start);
        result[0].BillingPeriodEndUtc.Should().Be(end);
    }

    [Fact]
    public async Task ListMyChargeHistoryAsync_WithSameDueDate_ShouldOrderByCreatedAtUtcDescending()
    {
        var sellerUserId = Guid.NewGuid();
        var dueDateUtc = new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc);
        var older = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        var newer = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);

        await SeedCharge(sellerUserId, "charge-older", dueDateUtc, createdAtUtc: older);
        await SeedCharge(sellerUserId, "charge-newer", dueDateUtc, createdAtUtc: newer);

        var result = await _sut.ListMyChargeHistoryAsync(sellerUserId);

        result.Select(x => x.GatewayChargeId).Should().Equal("charge-newer", "charge-older");
    }

    private async Task SeedCharge(
        Guid sellerUserId,
        string gatewayChargeId,
        DateTime dueDateUtc,
        DateTime? billingPeriodStartUtc = null,
        DateTime? billingPeriodEndUtc = null,
        DateTime? createdAtUtc = null)
    {
        var charge = new SellerSubscriptionChargeHistory
        {
            SellerUserId = sellerUserId,
            GatewayChargeId = gatewayChargeId,
            GatewayStatus = "RECEIVED",
            BillingStatus = SellerSubscriptionBillingStatus.Active,
            DueDateUtc = dueDateUtc,
            Amount = 49.90m,
            RawPayload = "{}",
            BillingPeriodStartUtc = billingPeriodStartUtc,
            BillingPeriodEndUtc = billingPeriodEndUtc
        };

        if (createdAtUtc is not null)
        {
            typeof(BaseEntity)
                .GetProperty(nameof(BaseEntity.CreatedAtUtc))!
                .SetValue(charge, createdAtUtc.Value);
        }

        _db.SellerSubscriptionChargeHistories.Add(charge);
        await _db.SaveChangesAsync();
    }
}
