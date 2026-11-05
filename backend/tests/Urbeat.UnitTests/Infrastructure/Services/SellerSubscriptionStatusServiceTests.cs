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

    [Fact]
    public async Task GetMySubscriptionAsync_ShouldExposeActiveTrialFromPlan()
    {
        var (sellerUserId, storeId, plan) = await SeedSubscriptionAsync(
            trialDays: 15,
            startUtc: DateTime.UtcNow.AddDays(-2));

        var result = await _sut.GetMySubscriptionAsync(sellerUserId);

        result.HasSubscription.Should().BeTrue();
        result.StoreId.Should().Be(storeId);
        result.PlanId.Should().Be(plan.Id);
        result.TrialDays.Should().Be(15);
        result.IsTrialActive.Should().BeTrue();
        result.HasGatewaySubscription.Should().BeFalse();
        result.TrialEndsAtUtc.Should().BeCloseTo(DateTime.UtcNow.AddDays(13), TimeSpan.FromMinutes(1));
        result.NextChargeAmount.Should().Be(plan.Amount);
        result.LastChargeStatus.Should().Be("Periodo gratuito");
    }

    [Fact]
    public async Task GetMySubscriptionAsync_ShouldNotReportTrial_WhenPlanHasZeroTrialDays()
    {
        var (sellerUserId, _, plan) = await SeedSubscriptionAsync(
            trialDays: 0,
            startUtc: DateTime.UtcNow);

        var result = await _sut.GetMySubscriptionAsync(sellerUserId);

        result.TrialDays.Should().Be(0);
        result.TrialEndsAtUtc.Should().BeNull();
        result.IsTrialActive.Should().BeFalse();
        result.LastChargeStatus.Should().Be("Nao contratado");
        result.NextChargeAmount.Should().Be(plan.Amount);
    }

    [Fact]
    public async Task GetMySubscriptionAsync_ShouldNotReportTrial_WhenTrialHasExpired()
    {
        var (sellerUserId, _, _) = await SeedSubscriptionAsync(
            trialDays: 15,
            startUtc: DateTime.UtcNow.AddDays(-30));

        var result = await _sut.GetMySubscriptionAsync(sellerUserId);

        result.TrialEndsAtUtc.Should().BeCloseTo(DateTime.UtcNow.AddDays(-15), TimeSpan.FromMinutes(1));
        result.IsTrialActive.Should().BeFalse();
        result.LastChargeStatus.Should().Be("Nao contratado");
    }

    [Fact]
    public async Task GetMySubscriptionAsync_ShouldNotReportTrial_WhenGatewayContractExists()
    {
        var (sellerUserId, _, _) = await SeedSubscriptionAsync(
            trialDays: 15,
            startUtc: DateTime.UtcNow,
            gatewaySubscriptionId: "sub_existing");

        var result = await _sut.GetMySubscriptionAsync(sellerUserId);

        result.HasGatewaySubscription.Should().BeTrue();
        result.IsTrialActive.Should().BeFalse();
        result.LastChargeStatus.Should().Be("Pago");
    }

    [Fact]
    public async Task GetMySubscriptionAsync_ShouldUseLocalNextBillingAsTrialEnd_WhenPlanIsMissing()
    {
        var sellerUserId = Guid.NewGuid();
        var store = new Store
        {
            OwnerUserId = sellerUserId,
            Name = "Loja Legada",
            Slug = $"loja-legada-{Guid.NewGuid():N}",
            PhoneNumber = "11999999999",
        };
        var nextBillingDateUtc = DateTime.UtcNow.AddDays(10);
        _db.Stores.Add(store);
        _db.SellerSubscriptions.Add(new SellerSubscription
        {
            StoreId = store.Id,
            SellerUserId = sellerUserId,
            PlanId = null,
            PlanName = BillingPlanSeeder.DefaultPlanName,
            PlanAmount = BillingPlanSeeder.DefaultPlanAmount,
            Status = SellerSubscriptionBillingStatus.Active,
            StartDateUtc = DateTime.UtcNow.AddDays(-1),
            NextBillingDateUtc = nextBillingDateUtc,
        });
        _db.SellerSubscriptionStatuses.Add(new SellerSubscriptionStatus
        {
            SellerUserId = sellerUserId,
            NextDueDateUtc = nextBillingDateUtc,
            BillingStatus = SellerSubscriptionBillingStatus.Active,
        });
        await _db.SaveChangesAsync();

        var result = await _sut.GetMySubscriptionAsync(sellerUserId);

        result.IsTrialActive.Should().BeTrue();
        result.TrialDays.Should().BeNull();
        result.TrialEndsAtUtc.Should().Be(nextBillingDateUtc);
        result.LastChargeStatus.Should().Be("Periodo gratuito");
    }

    [Fact]
    public async Task GetMySubscriptionAsync_ShouldExposeStoreId_WhenSellerHasNoSubscription()
    {
        var sellerUserId = Guid.NewGuid();
        var store = new Store
        {
            OwnerUserId = sellerUserId,
            Name = "Loja Sem Assinatura",
            Slug = $"loja-sem-assinatura-{Guid.NewGuid():N}",
            PhoneNumber = "11999999999",
        };
        _db.Stores.Add(store);
        await _db.SaveChangesAsync();

        var result = await _sut.GetMySubscriptionAsync(sellerUserId);

        result.HasSubscription.Should().BeFalse();
        result.StoreId.Should().Be(store.Id);
        result.IsTrialActive.Should().BeFalse();
        result.RegularizationMessage.Should().Contain("Contrate um plano");
    }

    private async Task<(Guid SellerUserId, Guid StoreId, Plan Plan)> SeedSubscriptionAsync(
        int trialDays,
        DateTime startUtc,
        string gatewaySubscriptionId = "")
    {
        var sellerUserId = Guid.NewGuid();
        var store = new Store
        {
            OwnerUserId = sellerUserId,
            Name = "Loja Assinatura",
            Slug = $"loja-assinatura-{Guid.NewGuid():N}",
            PhoneNumber = "11999999999",
        };
        var plan = new Plan
        {
            Name = $"Plano {Guid.NewGuid():N}",
            Amount = 49.90m,
            Description = "Plano de teste",
            IsActive = true,
            TrialDays = trialDays,
        };

        _db.Stores.Add(store);
        _db.Plans.Add(plan);

        var nextBillingDateUtc = startUtc.AddDays(trialDays > 0 ? trialDays : 30);
        _db.SellerSubscriptions.Add(new SellerSubscription
        {
            StoreId = store.Id,
            SellerUserId = sellerUserId,
            PlanId = plan.Id,
            PlanName = plan.Name,
            PlanAmount = plan.Amount,
            Status = SellerSubscriptionBillingStatus.Active,
            StartDateUtc = startUtc,
            NextBillingDateUtc = nextBillingDateUtc,
            GatewayCustomerId = string.IsNullOrWhiteSpace(gatewaySubscriptionId) ? string.Empty : "cus_existing",
            GatewaySubscriptionId = gatewaySubscriptionId,
        });
        _db.SellerSubscriptionStatuses.Add(new SellerSubscriptionStatus
        {
            SellerUserId = sellerUserId,
            NextDueDateUtc = nextBillingDateUtc,
            BillingStatus = SellerSubscriptionBillingStatus.Active,
        });

        await _db.SaveChangesAsync();
        return (sellerUserId, store.Id, plan);
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
