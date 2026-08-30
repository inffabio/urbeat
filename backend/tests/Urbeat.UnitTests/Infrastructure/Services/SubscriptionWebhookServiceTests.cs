using FluentAssertions;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Persistence.UnitOfWork;
using Urbeat.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.UnitTests.Infrastructure.Services;

public sealed class SubscriptionWebhookServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly SubscriptionWebhookService _sut;
    private readonly Guid _sellerUserId = Guid.NewGuid();

    public SubscriptionWebhookServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-subscription-webhook-{Guid.NewGuid()}")
            .Options;
        _db = new ApplicationDbContext(options);

        _sut = new SubscriptionWebhookService(
            _db,
            new EfUnitOfWork(_db),
            new FakeSubscriptionNotificationService(_db));
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task ProcessAsaasWebhookAsync_ShouldFillBillingPeriodOnFirstCharge()
    {
        var payload = BuildPayload("evt-001", "pay-001", "RECEIVED", "2026-08-10");

        await _sut.ProcessAsaasWebhookAsync(payload, "127.0.0.1");

        var charge = await _db.SellerSubscriptionChargeHistories
            .SingleAsync(x => x.GatewayChargeId == "pay-001");

        charge.BillingPeriodStartUtc.Should().Be(new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc));
        charge.BillingPeriodEndUtc.Should().Be(new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task ProcessAsaasWebhookAsync_ShouldPreserveBillingPeriodOnRepeatedWebhook()
    {
        await _sut.ProcessAsaasWebhookAsync(BuildPayload("evt-001", "pay-001", "RECEIVED", "2026-08-10"), "127.0.0.1");

        await _sut.ProcessAsaasWebhookAsync(BuildPayload("evt-002", "pay-001", "CONFIRMED", "2026-08-20"), "127.0.0.1");

        var charge = await _db.SellerSubscriptionChargeHistories
            .SingleAsync(x => x.GatewayChargeId == "pay-001");

        charge.BillingPeriodStartUtc.Should().Be(new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc));
        charge.BillingPeriodEndUtc.Should().Be(new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc));
        charge.GatewayStatus.Should().Be("CONFIRMED");
    }

    [Fact]
    public async Task ProcessAsaasWebhookAsync_ShouldFillMissingPeriodOnLegacyChargeUpdate()
    {
        _db.SellerSubscriptionChargeHistories.Add(new SellerSubscriptionChargeHistory
        {
            SellerUserId = _sellerUserId,
            GatewayChargeId = "pay-legacy",
            GatewayStatus = "PENDING",
            BillingStatus = SellerSubscriptionBillingStatus.Overdue,
            DueDateUtc = new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc),
            RawPayload = "{}"
        });
        await _db.SaveChangesAsync();

        await _sut.ProcessAsaasWebhookAsync(BuildPayload("evt-legacy", "pay-legacy", "RECEIVED", "2026-08-10"), "127.0.0.1");

        var charge = await _db.SellerSubscriptionChargeHistories
            .SingleAsync(x => x.GatewayChargeId == "pay-legacy");

        charge.BillingPeriodStartUtc.Should().Be(new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc));
        charge.BillingPeriodEndUtc.Should().Be(new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc));
    }

    private string BuildPayload(string eventId, string paymentId, string status, string dueDate)
    {
        return $$"""
        {
            "id": "{{eventId}}",
            "event": "PAYMENT_RECEIVED",
            "sellerUserId": "{{_sellerUserId}}",
            "payment": {
                "id": "{{paymentId}}",
                "externalReference": "{{_sellerUserId}}",
                "status": "{{status}}",
                "dueDate": "{{dueDate}}",
                "value": "49.90"
            }
        }
        """;
    }

    private sealed class FakeSubscriptionNotificationService : ISubscriptionNotificationService
    {
        private readonly ApplicationDbContext _db;

        public FakeSubscriptionNotificationService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task ProcessSellerSubscriptionNotificationsAsync(CancellationToken cancellationToken = default)
            => await _db.SaveChangesAsync(cancellationToken);
    }
}
