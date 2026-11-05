using AutoMapper;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Mappings;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Persistence.UnitOfWork;
using Urbeat.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Urbeat.UnitTests.Infrastructure.Services;

public sealed class StoreServiceSubscriptionTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly StoreService _sut;

    public StoreServiceSubscriptionTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-store-subscription-{Guid.NewGuid()}")
            .Options;
        _db = new ApplicationDbContext(options);

        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<EntityToDtoProfile>()).CreateMapper();

        _sut = new StoreService(
            _db,
            mapper,
            Mock.Of<IStoreReadRepository>(),
            new EfUnitOfWork(_db),
            Mock.Of<IImageUploadService>());
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task CreateForOwnerAsync_ShouldAttachBasicSubscriptionAndActiveStatus()
    {
        var cuisine = new CuisineType { Name = "Pizza", IsActive = true };
        _db.CuisineTypes.Add(cuisine);

        var plan = new Plan
        {
            Name = BillingPlanSeeder.DefaultPlanName,
            Amount = 49.90m,
            Description = BillingPlanSeeder.DefaultPlanDescription,
            IsActive = true
        };
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();

        var ownerUserId = Guid.NewGuid();

        var result = await _sut.CreateForOwnerAsync(
            ownerUserId,
            new CreateStoreRequestDto
            {
                Name = "Loja Teste",
                PhoneNumber = "11999999999",
                CuisineType = "Pizza"
            },
            ipAddress: null);

        result.Created.Should().BeTrue();
        result.Store.Should().NotBeNull();

        var store = await _db.Stores.SingleAsync(x => x.OwnerUserId == ownerUserId);
        store.IsSubscriptionBlocked.Should().BeFalse();

        var subscription = await _db.SellerSubscriptions.SingleAsync(x => x.StoreId == store.Id);
        subscription.SellerUserId.Should().Be(ownerUserId);
        subscription.PlanId.Should().Be(plan.Id);
        subscription.PlanName.Should().Be(BillingPlanSeeder.DefaultPlanName);
        subscription.PlanAmount.Should().Be(49.90m);
        subscription.Status.Should().Be(SellerSubscriptionBillingStatus.Active);
        subscription.StartDateUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        subscription.NextBillingDateUtc.Should().BeCloseTo(DateTime.UtcNow.AddDays(15), TimeSpan.FromMinutes(1));

        var status = await _db.SellerSubscriptionStatuses.SingleAsync(x => x.SellerUserId == ownerUserId);
        status.BillingStatus.Should().Be(SellerSubscriptionBillingStatus.Active);
    }

    [Fact]
    public async Task CreateForOwnerAsync_ShouldNotDuplicateSubscriptionOnRepeatedCall()
    {
        var cuisine = new CuisineType { Name = "Pizza", IsActive = true };
        _db.CuisineTypes.Add(cuisine);
        _db.Plans.Add(new Plan
        {
            Name = BillingPlanSeeder.DefaultPlanName,
            Amount = 49.90m,
            Description = BillingPlanSeeder.DefaultPlanDescription,
            IsActive = true
        });
        await _db.SaveChangesAsync();

        var ownerUserId = Guid.NewGuid();
        var request = new CreateStoreRequestDto
        {
            Name = "Loja Teste",
            PhoneNumber = "11999999999",
            CuisineType = "Pizza"
        };

        await _sut.CreateForOwnerAsync(ownerUserId, request, null);
        var second = await _sut.CreateForOwnerAsync(ownerUserId, request, null);

        second.AlreadyExists.Should().BeTrue();

        var subscriptions = await _db.SellerSubscriptions.Where(x => x.SellerUserId == ownerUserId).ToListAsync();
        subscriptions.Should().ContainSingle();
    }

    [Fact]
    public async Task CreateForOwnerAsync_ShouldUsePlanTrialDaysForFirstBillingDate()
    {
        _db.CuisineTypes.Add(new CuisineType { Name = "Pizza", IsActive = true });
        _db.Plans.Add(new Plan
        {
            Name = BillingPlanSeeder.DefaultPlanName,
            Amount = 19.90m,
            Description = "Plano Começar",
            IsActive = true,
            TrialDays = 7
        });
        await _db.SaveChangesAsync();

        var ownerUserId = Guid.NewGuid();

        var result = await _sut.CreateForOwnerAsync(
            ownerUserId,
            new CreateStoreRequestDto
            {
                Name = "Loja Trial",
                PhoneNumber = "11999999999",
                CuisineType = "Pizza"
            },
            ipAddress: null);

        result.Created.Should().BeTrue();

        var subscription = await _db.SellerSubscriptions.SingleAsync(x => x.SellerUserId == ownerUserId);
        subscription.StartDateUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        subscription.NextBillingDateUtc.Should().BeCloseTo(DateTime.UtcNow.AddDays(7), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task CreateForOwnerAsync_ShouldKeepMonthlyCycleWhenPlanHasNoTrial()
    {
        _db.CuisineTypes.Add(new CuisineType { Name = "Pizza", IsActive = true });
        _db.Plans.Add(new Plan
        {
            Name = BillingPlanSeeder.DefaultPlanName,
            Amount = 19.90m,
            Description = "Plano sem trial",
            IsActive = true,
            TrialDays = 0
        });
        await _db.SaveChangesAsync();

        var ownerUserId = Guid.NewGuid();

        var result = await _sut.CreateForOwnerAsync(
            ownerUserId,
            new CreateStoreRequestDto
            {
                Name = "Loja Sem Trial",
                PhoneNumber = "11999999999",
                CuisineType = "Pizza"
            },
            ipAddress: null);

        result.Created.Should().BeTrue();

        var subscription = await _db.SellerSubscriptions.SingleAsync(x => x.SellerUserId == ownerUserId);
        subscription.NextBillingDateUtc.Should().BeCloseTo(DateTime.UtcNow.AddMonths(1), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task CreateForOwnerAsync_ShouldSkipInactiveDefaultPlanAndUseActivePlan()
    {
        _db.CuisineTypes.Add(new CuisineType { Name = "Pizza", IsActive = true });
        _db.Plans.AddRange(
            new Plan
            {
                Name = BillingPlanSeeder.DefaultPlanName,
                Amount = 19.90m,
                Description = "Plano padrão inativo",
                IsActive = false,
                TrialDays = 5
            },
            new Plan
            {
                Name = "Plano Ativo",
                Amount = 39.90m,
                Description = "Plano ativo",
                IsActive = true,
                TrialDays = 10
            });
        await _db.SaveChangesAsync();

        var ownerUserId = Guid.NewGuid();

        var result = await _sut.CreateForOwnerAsync(
            ownerUserId,
            new CreateStoreRequestDto
            {
                Name = "Loja Plano Ativo",
                PhoneNumber = "11999999999",
                CuisineType = "Pizza"
            },
            ipAddress: null);

        result.Created.Should().BeTrue();

        var subscription = await _db.SellerSubscriptions.SingleAsync(x => x.SellerUserId == ownerUserId);
        subscription.PlanName.Should().Be("Plano Ativo");
        subscription.PlanAmount.Should().Be(39.90m);
        subscription.NextBillingDateUtc.Should().BeCloseTo(DateTime.UtcNow.AddDays(10), TimeSpan.FromMinutes(1));
    }
}
