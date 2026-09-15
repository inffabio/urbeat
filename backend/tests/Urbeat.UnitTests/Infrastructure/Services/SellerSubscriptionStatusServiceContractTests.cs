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
        _adapter
            .Setup(a => a.CreateContractAsync(It.IsAny<AsaasSubscriptionContractRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AsaasSubscriptionContractResponse
            {
                GatewayCustomerId = "cus_demo",
                GatewaySubscriptionId = "sub_demo",
                NextDueDateUtc = DateTime.UtcNow.AddDays(30)
            });

        _sut = new SellerSubscriptionStatusService(_db, new EfUnitOfWork(_db), _adapter.Object);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task ContractAsync_ShouldUpgradeLocalDefaultSubscription()
    {
        var (sellerUserId, storeId, plan) = await SeedSellerStoreAndPlanAsync();

        _db.SellerSubscriptions.Add(new SellerSubscription
        {
            StoreId = storeId,
            SellerUserId = sellerUserId,
            PlanId = plan.Id,
            PlanName = BillingPlanSeeder.DefaultPlanName,
            PlanAmount = 49.90m,
            Status = SellerSubscriptionBillingStatus.Active,
            StartDateUtc = DateTime.UtcNow,
            NextBillingDateUtc = DateTime.UtcNow.AddMonths(1),
            GatewayCustomerId = string.Empty,
            GatewaySubscriptionId = string.Empty
        });
        await _db.SaveChangesAsync();

        var result = await _sut.ContractAsync(
            sellerUserId,
            new ContractSellerSubscriptionRequestDto
            {
                StoreId = storeId,
                PlanId = plan.Id,
                FirstDueDateUtc = DateTime.UtcNow.AddDays(7)
            },
            ipAddress: null);

        result.AlreadyContracted.Should().BeFalse();
        result.Subscription.Should().NotBeNull();
        result.Subscription!.GatewaySubscriptionId.Should().Be("sub_demo");

        var subscriptions = await _db.SellerSubscriptions.Where(x => x.StoreId == storeId).ToListAsync();
        subscriptions.Should().ContainSingle();
        subscriptions[0].GatewaySubscriptionId.Should().Be("sub_demo");
    }

    [Fact]
    public async Task ContractAsync_ShouldReturnConflict_WhenAlreadyContractedWithGateway()
    {
        var (sellerUserId, storeId, plan) = await SeedSellerStoreAndPlanAsync();

        _db.SellerSubscriptions.Add(new SellerSubscription
        {
            StoreId = storeId,
            SellerUserId = sellerUserId,
            PlanId = plan.Id,
            PlanName = BillingPlanSeeder.DefaultPlanName,
            PlanAmount = 49.90m,
            Status = SellerSubscriptionBillingStatus.Active,
            StartDateUtc = DateTime.UtcNow,
            NextBillingDateUtc = DateTime.UtcNow.AddMonths(1),
            GatewayCustomerId = "cus_existing",
            GatewaySubscriptionId = "sub_existing"
        });
        await _db.SaveChangesAsync();

        var result = await _sut.ContractAsync(
            sellerUserId,
            new ContractSellerSubscriptionRequestDto
            {
                StoreId = storeId,
                PlanId = plan.Id,
                FirstDueDateUtc = DateTime.UtcNow.AddDays(7)
            },
            ipAddress: null);

        result.AlreadyContracted.Should().BeTrue();
        result.Subscription.Should().BeNull();
    }

    private async Task<(Guid SellerUserId, Guid StoreId, Plan Plan)> SeedSellerStoreAndPlanAsync()
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
            Name = "Plano Pro",
            Amount = 59.90m,
            Description = "Plano Pro description",
            IsActive = true
        };
        _db.Plans.Add(plan);

        await _db.SaveChangesAsync();

        return (sellerUserId, store.Id, plan);
    }
}
