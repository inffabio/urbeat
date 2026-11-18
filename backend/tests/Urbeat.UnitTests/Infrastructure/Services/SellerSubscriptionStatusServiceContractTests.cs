using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Persistence.UnitOfWork;
using Urbeat.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Moq;
using Npgsql;

namespace Urbeat.UnitTests.Infrastructure.Services;

public sealed class SellerSubscriptionStatusServiceContractTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly Mock<IAsaasSubscriptionAdapter> _adapter;
    private readonly SellerSubscriptionStatusService _sut;

    public SellerSubscriptionStatusServiceContractTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-contract-{Guid.NewGuid()}")
            .Options;
        _db = new ApplicationDbContext(options);

        _adapter = new Mock<IAsaasSubscriptionAdapter>();
        _sut = new SellerSubscriptionStatusService(_db, new EfUnitOfWork(_db), _adapter.Object);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task ContractAsync_ShouldCreateLocalSubscriptionAndAwaitingGatewayStub()
    {
        var (sellerUserId, storeId, plan) = await SeedSellerStoreAndPlanAsync(level: 1);

        var result = await _sut.ContractAsync(
            sellerUserId,
            new ContractSellerSubscriptionRequestDto
            {
                StoreId = storeId,
                PlanId = plan.Id,
                BillingPeriod = SubscriptionBillingPeriod.Monthly
            },
            ipAddress: null);

        result.Subscription.Should().NotBeNull();
        result.Subscription!.GatewaySubscriptionId.Should().BeEmpty();
        result.Subscription.AwaitingGateway.Should().BeTrue();
        result.Subscription.BillingPeriod.Should().Be(SubscriptionBillingPeriod.Monthly);
        result.Subscription.PlanAmount.Should().Be(plan.Amount);
        result.Subscription.TrialEndsAtUtc.Should().NotBeNull();

        var subscriptions = await _db.SellerSubscriptions.Where(x => x.StoreId == storeId).ToListAsync();
        subscriptions.Should().ContainSingle();
        subscriptions[0].PlanName.Should().Be(plan.Name);
        subscriptions[0].PlanDescriptionSnapshot.Should().Be(plan.Description);
        subscriptions[0].TrialDaysSnapshot.Should().Be(plan.TrialDays);
        subscriptions[0].BillingPeriod.Should().Be(SubscriptionBillingPeriod.Monthly);

        var charge = await _db.SellerSubscriptionCharges.SingleAsync();
        charge.Status.Should().Be(SellerSubscriptionChargeStatus.AwaitingGateway);
        charge.IdempotencyKey.Should().NotBeNullOrWhiteSpace();
        charge.Amount.Should().Be(plan.Amount);

        _adapter.Verify(
            a => a.CreateContractAsync(It.IsAny<AsaasSubscriptionContractRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ContractAsync_ShouldAllowUpgradeToSuperiorLevelAndPreserveHistory()
    {
        var (sellerUserId, storeId, currentPlan) = await SeedSellerStoreAndPlanAsync(level: 1);
        var superiorPlan = new Plan
        {
            Name = "Premium",
            Amount = 99.90m,
            Description = "Plano superior",
            IsActive = true,
            Level = 3,
            MonthlyEnabled = true,
            AnnualEnabled = true,
            AnnualDiscountPercent = 20
        };
        _db.Plans.Add(superiorPlan);

        _db.SellerSubscriptions.Add(new SellerSubscription
        {
            StoreId = storeId,
            SellerUserId = sellerUserId,
            PlanId = currentPlan.Id,
            PlanName = currentPlan.Name,
            PlanDescriptionSnapshot = currentPlan.Description,
            PlanAmount = currentPlan.Amount,
            BillingPeriod = SubscriptionBillingPeriod.Monthly,
            PlanLevelSnapshot = currentPlan.Level,
            Status = SellerSubscriptionBillingStatus.Active,
            StartDateUtc = DateTime.UtcNow.AddDays(-30),
            NextBillingDateUtc = DateTime.UtcNow.AddDays(5)
        });
        await _db.SaveChangesAsync();

        var result = await _sut.ContractAsync(
            sellerUserId,
            new ContractSellerSubscriptionRequestDto
            {
                StoreId = storeId,
                PlanId = superiorPlan.Id,
                BillingPeriod = SubscriptionBillingPeriod.Annual
            },
            ipAddress: null);

        result.Subscription.Should().NotBeNull();
        result.Subscription!.PlanName.Should().Be("Premium");
        result.Subscription.BillingPeriod.Should().Be(SubscriptionBillingPeriod.Annual);
        result.Subscription.PlanAmount.Should().Be(superiorPlan.AnnualAmount);

        var persisted = await _db.SellerSubscriptions.SingleAsync(x => x.StoreId == storeId);
        persisted.PlanId.Should().Be(superiorPlan.Id);
        persisted.PlanLevelSnapshot.Should().Be(3);
        persisted.AnnualDiscountPercentSnapshot.Should().Be(20);

        var change = await _db.SellerSubscriptionChanges.SingleAsync();
        change.PreviousPlanName.Should().Be(currentPlan.Name);
        change.NewPlanName.Should().Be("Premium");
        change.PreviousPlanLevel.Should().Be(currentPlan.Level);
        change.NewPlanLevel.Should().Be(3);

        (await _db.SellerSubscriptionCharges.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ContractAsync_ShouldRejectDowngradeAndSameLevel()
    {
        var (sellerUserId, storeId, currentPlan) = await SeedSellerStoreAndPlanAsync(level: 5);
        var lowerPlan = new Plan
        {
            Name = "Básico",
            Amount = 9.90m,
            Description = "Plano inferior",
            IsActive = true,
            Level = 1,
            MonthlyEnabled = true
        };
        _db.Plans.Add(lowerPlan);

        _db.SellerSubscriptions.Add(new SellerSubscription
        {
            StoreId = storeId,
            SellerUserId = sellerUserId,
            PlanId = currentPlan.Id,
            PlanName = currentPlan.Name,
            PlanAmount = currentPlan.Amount,
            PlanLevelSnapshot = 5,
            Status = SellerSubscriptionBillingStatus.Active,
            StartDateUtc = DateTime.UtcNow,
            NextBillingDateUtc = DateTime.UtcNow.AddDays(5)
        });
        await _db.SaveChangesAsync();

        var downgrade = await _sut.ContractAsync(
            sellerUserId,
            new ContractSellerSubscriptionRequestDto
            {
                StoreId = storeId,
                PlanId = lowerPlan.Id,
                BillingPeriod = SubscriptionBillingPeriod.Monthly
            },
            ipAddress: null);

        downgrade.DowngradeNotAllowed.Should().BeTrue();
        downgrade.Subscription.Should().BeNull();

        var persisted = await _db.SellerSubscriptions.SingleAsync(x => x.StoreId == storeId);
        persisted.PlanId.Should().Be(currentPlan.Id);
        (await _db.SellerSubscriptionChanges.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ContractAsync_ShouldNotGrantNewTrialOnUpgrade()
    {
        var (sellerUserId, storeId, currentPlan) = await SeedSellerStoreAndPlanAsync(level: 1);
        var superiorPlan = new Plan
        {
            Name = "Premium",
            Amount = 99.90m,
            Description = "Superior",
            IsActive = true,
            Level = 2,
            TrialDays = 30
        };
        _db.Plans.Add(superiorPlan);

        _db.SellerSubscriptions.Add(new SellerSubscription
        {
            StoreId = storeId,
            SellerUserId = sellerUserId,
            PlanId = currentPlan.Id,
            PlanName = currentPlan.Name,
            PlanAmount = currentPlan.Amount,
            PlanLevelSnapshot = currentPlan.Level,
            Status = SellerSubscriptionBillingStatus.Active,
            StartDateUtc = DateTime.UtcNow.AddDays(-20),
            NextBillingDateUtc = DateTime.UtcNow.AddDays(2)
        });
        await _db.SaveChangesAsync();

        var result = await _sut.ContractAsync(
            sellerUserId,
            new ContractSellerSubscriptionRequestDto
            {
                StoreId = storeId,
                PlanId = superiorPlan.Id,
                BillingPeriod = SubscriptionBillingPeriod.Monthly
            },
            ipAddress: null);

        result.Subscription!.TrialEndsAtUtc.Should().BeNull();
        result.Subscription.NextBillingDateUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task ContractAsync_ShouldNotUnblockStoreOrClearOverdueStatusOnUpgrade()
    {
        var (sellerUserId, storeId, currentPlan) = await SeedSellerStoreAndPlanAsync(level: 1);
        var superiorPlan = new Plan
        {
            Name = "Premium bloqueio",
            Amount = 99.90m,
            Description = "Superior",
            IsActive = true,
            Level = 2
        };
        _db.Plans.Add(superiorPlan);

        var store = await _db.Stores.SingleAsync(x => x.Id == storeId);
        store.IsSubscriptionBlocked = true;

        _db.SellerSubscriptions.Add(new SellerSubscription
        {
            StoreId = storeId,
            SellerUserId = sellerUserId,
            PlanId = currentPlan.Id,
            PlanName = currentPlan.Name,
            PlanAmount = currentPlan.Amount,
            PlanLevelSnapshot = currentPlan.Level,
            Status = SellerSubscriptionBillingStatus.Blocked,
            StartDateUtc = DateTime.UtcNow.AddDays(-40),
            NextBillingDateUtc = DateTime.UtcNow.AddDays(-5)
        });
        _db.SellerSubscriptionStatuses.Add(new SellerSubscriptionStatus
        {
            SellerUserId = sellerUserId,
            NextDueDateUtc = DateTime.UtcNow.AddDays(-5),
            BillingStatus = SellerSubscriptionBillingStatus.Blocked
        });
        await _db.SaveChangesAsync();

        var result = await _sut.ContractAsync(
            sellerUserId,
            new ContractSellerSubscriptionRequestDto
            {
                StoreId = storeId,
                PlanId = superiorPlan.Id,
                BillingPeriod = SubscriptionBillingPeriod.Monthly
            },
            ipAddress: null);

        result.Subscription.Should().NotBeNull();
        (await _db.Stores.SingleAsync(x => x.Id == storeId)).IsSubscriptionBlocked.Should().BeTrue();
        (await _db.SellerSubscriptionStatuses.SingleAsync(x => x.SellerUserId == sellerUserId))
            .BillingStatus.Should().Be(SellerSubscriptionBillingStatus.Blocked);
    }

    [Fact]
    public async Task ContractAsync_ShouldPreserveLegacyGatewayIdentifiersOnUpgrade()
    {
        var (sellerUserId, storeId, currentPlan) = await SeedSellerStoreAndPlanAsync(level: 1);
        var superiorPlan = new Plan
        {
            Name = "Premium legado",
            Amount = 99.90m,
            Description = "Superior",
            IsActive = true,
            Level = 2
        };
        _db.Plans.Add(superiorPlan);

        _db.SellerSubscriptions.Add(new SellerSubscription
        {
            StoreId = storeId,
            SellerUserId = sellerUserId,
            PlanId = currentPlan.Id,
            PlanName = currentPlan.Name,
            PlanAmount = currentPlan.Amount,
            PlanLevelSnapshot = currentPlan.Level,
            Status = SellerSubscriptionBillingStatus.Active,
            StartDateUtc = DateTime.UtcNow.AddDays(-30),
            NextBillingDateUtc = DateTime.UtcNow.AddDays(2),
            GatewayCustomerId = "cus_legacy",
            GatewaySubscriptionId = "sub_legacy"
        });
        await _db.SaveChangesAsync();

        var result = await _sut.ContractAsync(
            sellerUserId,
            new ContractSellerSubscriptionRequestDto
            {
                StoreId = storeId,
                PlanId = superiorPlan.Id,
                BillingPeriod = SubscriptionBillingPeriod.Monthly
            },
            ipAddress: null);

        result.Subscription.Should().NotBeNull();
        var persisted = await _db.SellerSubscriptions.SingleAsync(x => x.StoreId == storeId);
        persisted.GatewayCustomerId.Should().Be("cus_legacy");
        persisted.GatewaySubscriptionId.Should().Be("sub_legacy");
    }

    [Fact]
    public async Task ContractAsync_ShouldReturnConflictInsteadOf500_WhenConcurrentInsertHitsUniqueConstraint()
    {
        var (sellerUserId, storeId, plan) = await SeedSellerStoreAndPlanAsync(level: 1);

        var uow = new Mock<IEfUnitOfWork>();
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException(
                "concurrent insert",
                new PostgresException(
                    "duplicate key value violates unique constraint \"IX_SellerSubscriptions_StoreId\"",
                    "ERROR",
                    "ERROR",
                    PostgresErrorCodes.UniqueViolation)));

        var sut = new SellerSubscriptionStatusService(_db, uow.Object, _adapter.Object);

        var result = await sut.ContractAsync(
            sellerUserId,
            new ContractSellerSubscriptionRequestDto
            {
                StoreId = storeId,
                PlanId = plan.Id,
                BillingPeriod = SubscriptionBillingPeriod.Monthly
            },
            ipAddress: null);

        result.AlreadyContracted.Should().BeTrue();
        result.Subscription.Should().BeNull();
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ContractAsync_ShouldRejectDisabledBillingPeriod()
    {
        var (sellerUserId, storeId, plan) = await SeedSellerStoreAndPlanAsync(level: 1);
        plan.AnnualEnabled = false;
        await _db.SaveChangesAsync();

        var result = await _sut.ContractAsync(
            sellerUserId,
            new ContractSellerSubscriptionRequestDto
            {
                StoreId = storeId,
                PlanId = plan.Id,
                BillingPeriod = SubscriptionBillingPeriod.Annual
            },
            ipAddress: null);

        result.InvalidBillingPeriod.Should().BeTrue();
        result.Subscription.Should().BeNull();
    }

    [Fact]
    public async Task ContractAsync_ShouldReturnConflict_WhenSamePlanIsRequestedAgain()
    {
        var (sellerUserId, storeId, plan) = await SeedSellerStoreAndPlanAsync(level: 1);

        _db.SellerSubscriptions.Add(new SellerSubscription
        {
            StoreId = storeId,
            SellerUserId = sellerUserId,
            PlanId = plan.Id,
            PlanName = plan.Name,
            PlanAmount = plan.Amount,
            PlanLevelSnapshot = plan.Level,
            Status = SellerSubscriptionBillingStatus.Active,
            StartDateUtc = DateTime.UtcNow,
            NextBillingDateUtc = DateTime.UtcNow.AddDays(15)
        });
        await _db.SaveChangesAsync();

        var result = await _sut.ContractAsync(
            sellerUserId,
            new ContractSellerSubscriptionRequestDto
            {
                StoreId = storeId,
                PlanId = plan.Id,
                BillingPeriod = SubscriptionBillingPeriod.Monthly
            },
            ipAddress: null);

        result.AlreadyContracted.Should().BeTrue();
    }

    [Fact]
    public void CalculateFirstDueDateUtc_ShouldNotExtendExpiredTrial()
    {
        var now = DateTime.UtcNow;

        var dueDate = SellerSubscriptionStatusService.CalculateFirstDueDateUtc(
            trialDays: 15,
            trialStartUtc: now.AddDays(-40),
            nowUtc: now);

        dueDate.Should().Be(now);
    }

    [Fact]
    public async Task ContractAsync_ShouldReturnForbidden_WhenStoreBelongsToAnotherSeller()
    {
        var (_, storeId, plan) = await SeedSellerStoreAndPlanAsync(level: 1);
        var otherSellerUserId = Guid.NewGuid();

        var result = await _sut.ContractAsync(
            otherSellerUserId,
            new ContractSellerSubscriptionRequestDto
            {
                StoreId = storeId,
                PlanId = plan.Id,
            },
            ipAddress: null);

        result.Forbidden.Should().BeTrue();
        result.Subscription.Should().BeNull();
    }

    [Fact]
    public async Task ContractAsync_ShouldReturnInvalidPlan_WhenPlanIsInactive()
    {
        var (sellerUserId, storeId, _) = await SeedSellerStoreAndPlanAsync(level: 1);
        var inactivePlan = new Plan
        {
            Name = "Plano Inativo",
            Amount = 19.90m,
            Description = "Inativo",
            IsActive = false,
        };
        _db.Plans.Add(inactivePlan);
        await _db.SaveChangesAsync();

        var result = await _sut.ContractAsync(
            sellerUserId,
            new ContractSellerSubscriptionRequestDto
            {
                StoreId = storeId,
                PlanId = inactivePlan.Id,
            },
            ipAddress: null);

        result.InvalidPlan.Should().BeTrue();
        result.Subscription.Should().BeNull();
    }

    [Fact]
    public async Task ContractAsync_ShouldReturnNotFound_WhenStoreDoesNotExist()
    {
        var (sellerUserId, _, plan) = await SeedSellerStoreAndPlanAsync(level: 1);

        var result = await _sut.ContractAsync(
            sellerUserId,
            new ContractSellerSubscriptionRequestDto
            {
                StoreId = Guid.NewGuid(),
                PlanId = plan.Id,
            },
            ipAddress: null);

        result.NotFound.Should().BeTrue();
        result.Subscription.Should().BeNull();
    }

    private async Task<(Guid SellerUserId, Guid StoreId, Plan Plan)> SeedSellerStoreAndPlanAsync(int level)
    {
        var sellerUserId = Guid.NewGuid();
        _db.Users.Add(new IdentityUser<Guid>
        {
            Id = sellerUserId,
            UserName = $"seller-{sellerUserId:N}@test.com",
            Email = $"seller-{sellerUserId:N}@test.com"
        });

        var store = new Store
        {
            OwnerUserId = sellerUserId,
            Name = "Loja Teste",
            Slug = $"loja-{sellerUserId:N}",
            PhoneNumber = "11999999999",
        };
        _db.Stores.Add(store);

        var plan = new Plan
        {
            Name = $"Plano Pro {Guid.NewGuid():N}",
            Amount = 59.90m,
            Description = "Plano Pro description",
            IsActive = true,
            Level = level,
            MonthlyEnabled = true,
            AnnualEnabled = true,
            AnnualDiscountPercent = 20
        };
        _db.Plans.Add(plan);

        await _db.SaveChangesAsync();

        return (sellerUserId, store.Id, plan);
    }
}
