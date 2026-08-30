using FluentAssertions;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.UnitTests.Infrastructure.Persistence;

public sealed class BillingPlanSeederTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly BillingPlanSeeder _sut;

    public BillingPlanSeederTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-billing-plan-{Guid.NewGuid()}")
            .Options;
        _db = new ApplicationDbContext(options);
        _sut = new BillingPlanSeeder(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task SeedAsync_ShouldCreateDefaultPlan()
    {
        var plan = await _sut.SeedAsync();

        plan.Should().NotBeNull();
        plan.Name.Should().Be(BillingPlanSeeder.DefaultPlanName);
        plan.Amount.Should().Be(49.90m);
        plan.IsActive.Should().BeTrue();
        plan.Description.Should().NotBeNullOrWhiteSpace();

        var persisted = await _db.Plans.SingleAsync(x => x.Name == BillingPlanSeeder.DefaultPlanName);
        persisted.Amount.Should().Be(49.90m);
    }

    [Fact]
    public async Task SeedAsync_ShouldBeIdempotent()
    {
        var first = await _sut.SeedAsync();
        var second = await _sut.SeedAsync();

        second.Id.Should().Be(first.Id);

        var plans = await _db.Plans.Where(x => x.Name == BillingPlanSeeder.DefaultPlanName).ToListAsync();
        plans.Should().ContainSingle();
    }

    [Fact]
    public async Task SeedAsync_ShouldPreserveExistingPlan()
    {
        var existing = new Plan
        {
            Name = BillingPlanSeeder.DefaultPlanName,
            Amount = 199.90m,
            Description = "Plano customizado existente",
            IsActive = true
        };
        _db.Plans.Add(existing);
        await _db.SaveChangesAsync();

        var result = await _sut.SeedAsync();

        result.Id.Should().Be(existing.Id);
        result.Amount.Should().Be(199.90m);
        result.Description.Should().Be("Plano customizado existente");

        var plans = await _db.Plans.Where(x => x.Name == BillingPlanSeeder.DefaultPlanName).ToListAsync();
        plans.Should().ContainSingle();
    }

    [Fact]
    public async Task SeedAsync_ShouldReactivateExistingInactivePlan()
    {
        var existing = new Plan
        {
            Name = BillingPlanSeeder.DefaultPlanName,
            Amount = 49.90m,
            Description = BillingPlanSeeder.DefaultPlanDescription,
            IsActive = false
        };
        _db.Plans.Add(existing);
        await _db.SaveChangesAsync();

        var result = await _sut.SeedAsync();

        result.Id.Should().Be(existing.Id);
        result.IsActive.Should().BeTrue();

        var plans = await _db.Plans.Where(x => x.Name == BillingPlanSeeder.DefaultPlanName).ToListAsync();
        plans.Should().ContainSingle();
        plans.Single().IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task SeedAsync_ShouldNotRemoveOtherPlans()
    {
        _db.Plans.Add(new Plan
        {
            Name = "Plano Premium",
            Amount = 99.90m,
            Description = "Taxa zero por pedido.",
            IsActive = true
        });
        await _db.SaveChangesAsync();

        await _sut.SeedAsync();

        var planNames = await _db.Plans.Select(x => x.Name).ToListAsync();
        planNames.Should().Contain("Plano Premium");
        planNames.Should().Contain(BillingPlanSeeder.DefaultPlanName);
    }
}
