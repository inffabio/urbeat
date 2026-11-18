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
    public async Task SeedAsync_ShouldCreateDefaultPlanCatalog()
    {
        var plan = await _sut.SeedAsync();

        plan.Should().NotBeNull();
        plan.Name.Should().Be(BillingPlanSeeder.DefaultPlanName);
        plan.Amount.Should().Be(BillingPlanSeeder.DefaultPlanAmount);
        plan.IsActive.Should().BeTrue();
        plan.Description.Should().NotBeNullOrWhiteSpace();
        plan.TrialDays.Should().Be(Plan.DefaultTrialDays);

        var persisted = await _db.Plans.SingleAsync(x => x.Name == BillingPlanSeeder.DefaultPlanName);
        persisted.Amount.Should().Be(BillingPlanSeeder.DefaultPlanAmount);

        var names = await _db.Plans.Select(x => x.Name).ToListAsync();
        names.Should().BeEquivalentTo(BillingPlanSeeder.Catalog.Select(x => x.Name));
    }

    [Fact]
    public async Task SeedAsync_ShouldCreateOnlyTheInitialPlanOnEmptyDatabase()
    {
        await _sut.SeedAsync();

        var plans = await _db.Plans.ToListAsync();

        plans.Should().ContainSingle();
        plans.Single().Name.Should().Be(BillingPlanSeeder.DefaultPlanName);
        BillingPlanSeeder.Catalog.Should().ContainSingle()
            .Which.Name.Should().Be(BillingPlanSeeder.DefaultPlanName);
    }

    [Fact]
    public async Task SeedAsync_ShouldPreserveAdditionalExistingPlansAndTheirConfiguration()
    {
        var existing = new Plan
        {
            Name = "Crescer",
            Amount = 123.45m,
            Description = "Plano configurado pelo admin",
            IsActive = false,
            Level = 7,
            AnnualDiscountPercent = 42
        };
        _db.Plans.Add(existing);
        await _db.SaveChangesAsync();

        await _sut.SeedAsync();

        var plans = await _db.Plans.ToListAsync();

        plans.Should().HaveCount(2);
        plans.Should().ContainSingle(x => x.Name == BillingPlanSeeder.DefaultPlanName);

        var preserved = plans.Single(x => x.Name == "Crescer");
        preserved.Id.Should().Be(existing.Id);
        preserved.Amount.Should().Be(123.45m);
        preserved.Description.Should().Be("Plano configurado pelo admin");
        preserved.IsActive.Should().BeFalse();
        preserved.Level.Should().Be(7);
        preserved.AnnualDiscountPercent.Should().Be(42);
    }

    [Fact]
    public async Task SeedAsync_ShouldBeIdempotent()
    {
        var first = await _sut.SeedAsync();
        var second = await _sut.SeedAsync();

        second.Id.Should().Be(first.Id);

        var plans = await _db.Plans
            .Where(x => x.Name == BillingPlanSeeder.DefaultPlanName)
            .ToListAsync();
        plans.Should().ContainSingle();

        foreach (var seed in BillingPlanSeeder.Catalog)
        {
            var count = await _db.Plans.CountAsync(x => x.Name == seed.Name);
            count.Should().Be(1);
        }
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
    public async Task SeedAsync_ShouldPreserveAdminDecisionForExistingInactivePlan()
    {
        var existing = new Plan
        {
            Name = BillingPlanSeeder.DefaultPlanName,
            Amount = 19.90m,
            Description = BillingPlanSeeder.DefaultPlanDescription,
            IsActive = false
        };
        _db.Plans.Add(existing);
        await _db.SaveChangesAsync();

        var result = await _sut.SeedAsync();

        result.Id.Should().Be(existing.Id);
        result.IsActive.Should().BeFalse();

        var plans = await _db.Plans.Where(x => x.Name == BillingPlanSeeder.DefaultPlanName).ToListAsync();
        plans.Should().ContainSingle();
        plans.Single().IsActive.Should().BeFalse();
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
