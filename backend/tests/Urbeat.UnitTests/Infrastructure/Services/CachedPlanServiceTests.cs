using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Plans;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Persistence.UnitOfWork;
using Urbeat.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Urbeat.UnitTests.Infrastructure.Services;

public sealed class CachedPlanServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly PlanService _planService;
    private readonly PublicPlanOfferCache _cache;
    private readonly ManualTimeProvider _clock;
    private readonly CachedPlanService _sut;
    private readonly ServiceProvider _provider;

    public CachedPlanServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-cached-plan-service-{Guid.NewGuid()}")
            .Options;
        _db = new ApplicationDbContext(options);

        _clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        _cache = new PublicPlanOfferCache(TimeSpan.FromSeconds(30), _clock);

        // The cached read runs in an isolated scope. Registering the shared context as a singleton
        // keeps test scopes from disposing the fixture database between assertions.
        var services = new ServiceCollection();
        services.AddSingleton(_db);
        services.AddSingleton<IEfUnitOfWork>(new EfUnitOfWork(_db));
        services.AddScoped<PlanService>();
        _provider = services.BuildServiceProvider();

        _planService = _provider.GetRequiredService<PlanService>();
        _sut = new CachedPlanService(
            _planService,
            _cache,
            _provider.GetRequiredService<IServiceScopeFactory>());
    }

    public void Dispose()
    {
        _provider.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task GetPublicOfferAsync_ShouldServeCachedOffer_UntilInvalidated()
    {
        var plan = AddActivePlan("Ativo", 19.90m);
        await _db.SaveChangesAsync();

        var first = await _sut.GetPublicOfferAsync();
        first.Plans.Should().ContainSingle(x => x.Id == plan.Id);

        await _sut.UpdateStatusAsync(plan.Id, isActive: false);

        var afterWrite = await _sut.GetPublicOfferAsync();
        afterWrite.Plans.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPublicOfferAsync_ShouldReloadOutOfBandChanges_AfterLifetime()
    {
        AddActivePlan("Antes", 19.90m);
        await _db.SaveChangesAsync();

        var first = await _sut.GetPublicOfferAsync();
        first.Plans.Should().ContainSingle();

        // Simulates a write performed outside this API process; only the TTL can make it visible.
        AddActivePlan("Depois", 29.90m);
        await _db.SaveChangesAsync();

        var cached = await _sut.GetPublicOfferAsync();
        cached.Plans.Should().ContainSingle();

        _clock.UtcNow = _clock.UtcNow.AddSeconds(31);

        var refreshed = await _sut.GetPublicOfferAsync();
        refreshed.Plans.Should().HaveCount(2);
        refreshed.Plans.Select(x => x.Name).Should().BeEquivalentTo("Antes", "Depois");
    }

    [Fact]
    public async Task CreateAsync_ShouldInvalidateCachedOffer()
    {
        _ = await _sut.GetPublicOfferAsync();

        await _sut.CreateAsync(new CreatePlanRequestDto
        {
            Name = "Novo",
            Amount = 39.90m,
            Description = "Criado depois do cache",
            IsActive = true
        });

        var offer = await _sut.GetPublicOfferAsync();
        offer.Plans.Should().ContainSingle(x => x.Name == "Novo");
    }

    [Fact]
    public async Task UpdateAsync_ShouldInvalidateCachedOffer()
    {
        var plan = AddActivePlan("Crescer", 39.90m);
        await _db.SaveChangesAsync();

        var first = await _sut.GetPublicOfferAsync();
        first.Plans.Single().Amount.Should().Be(39.90m);

        await _sut.UpdateAsync(plan.Id, new UpdatePlanRequestDto
        {
            Name = "Crescer",
            Amount = 49.90m,
            Description = "Preço atualizado"
        });

        var updated = await _sut.GetPublicOfferAsync();
        updated.Plans.Single().Amount.Should().Be(49.90m);
    }

    private Plan AddActivePlan(string name, decimal amount)
    {
        var plan = new Plan
        {
            Name = name,
            Amount = amount,
            Description = name,
            IsActive = true,
            TrialDays = Plan.DefaultTrialDays,
            BenefitsJson = PlanBenefits.Serialize(["Benefício"])
        };

        _db.Plans.Add(plan);
        return plan;
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        public ManualTimeProvider(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; set; }

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
