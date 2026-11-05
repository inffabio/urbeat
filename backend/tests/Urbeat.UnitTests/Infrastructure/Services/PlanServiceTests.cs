using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Application.Plans;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Persistence.UnitOfWork;
using Urbeat.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.UnitTests.Infrastructure.Services;

public sealed class PlanServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly PlanService _sut;

    public PlanServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-plan-service-{Guid.NewGuid()}")
            .Options;
        _db = new ApplicationDbContext(options);
        _sut = new PlanService(_db, new EfUnitOfWork(_db));
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task ListActiveAsync_ShouldReturnOnlyActivePlansWithTrialAndBenefits()
    {
        _db.Plans.AddRange(
            new Plan
            {
                Name = "Crescer",
                Amount = 39.90m,
                Description = "Plano intermediário",
                IsActive = true,
                TrialDays = 10,
                BenefitsJson = PlanBenefits.Serialize(["Relatórios", "Clientes"])
            },
            new Plan
            {
                Name = "Inativo",
                Amount = 9.90m,
                Description = "Não deve aparecer",
                IsActive = false,
                TrialDays = 5,
                BenefitsJson = "[]"
            });
        await _db.SaveChangesAsync();

        var plans = await _sut.ListActiveAsync();

        plans.Should().ContainSingle();
        plans[0].Name.Should().Be("Crescer");
        plans[0].TrialDays.Should().Be(10);
        plans[0].Benefits.Should().Equal("Relatórios", "Clientes");
    }

    [Fact]
    public async Task CreateAsync_ShouldPersistTrialAndNormalizedBenefits()
    {
        var result = await _sut.CreateAsync(new CreatePlanRequestDto
        {
            Name = "  Começar  ",
            Amount = 19.90m,
            Description = "  Plano de entrada  ",
            TrialDays = 7,
            Benefits = [" Cardápio digital ", "Cardápio digital", "WhatsApp"]
        });

        result.Should().NotBeNull();
        result!.Name.Should().Be("Começar");
        result.Description.Should().Be("Plano de entrada");
        result.TrialDays.Should().Be(7);
        result.Benefits.Should().Equal("Cardápio digital", "WhatsApp");

        var persisted = await _db.Plans.SingleAsync();
        persisted.TrialDays.Should().Be(7);
        PlanBenefits.Deserialize(persisted.BenefitsJson).Should().Equal("Cardápio digital", "WhatsApp");
    }

    [Fact]
    public async Task CreateAsync_ShouldUseDefaultTrialWhenOmitted()
    {
        var result = await _sut.CreateAsync(new CreatePlanRequestDto
        {
            Name = "Sem trial informado",
            Amount = 29.90m,
            Description = "Plano"
        });

        result.Should().NotBeNull();
        result!.TrialDays.Should().Be(Plan.DefaultTrialDays);
        result.Benefits.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateAsync_ShouldPreserveOmittedTrialAndBenefits()
    {
        var plan = new Plan
        {
            Name = "Crescer",
            Amount = 39.90m,
            Description = "Plano intermediário",
            IsActive = true,
            TrialDays = 12,
            BenefitsJson = PlanBenefits.Serialize(["Relatórios"])
        };
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();

        var result = await _sut.UpdateAsync(plan.Id, new UpdatePlanRequestDto
        {
            Name = "Crescer Plus",
            Amount = 44.90m,
            Description = "Atualizado"
        });

        result.Should().NotBeNull();
        result!.TrialDays.Should().Be(12);
        result.Benefits.Should().Equal("Relatórios");

        var persisted = await _db.Plans.SingleAsync();
        persisted.TrialDays.Should().Be(12);
        PlanBenefits.Deserialize(persisted.BenefitsJson).Should().Equal("Relatórios");
    }

    [Fact]
    public async Task UpdateAsync_ShouldReplaceBenefitsWhenProvided()
    {
        var plan = new Plan
        {
            Name = "Profissional",
            Amount = 79.90m,
            Description = "Plano avançado",
            IsActive = true,
            TrialDays = 15,
            BenefitsJson = PlanBenefits.Serialize(["Antigo"])
        };
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();

        var result = await _sut.UpdateAsync(plan.Id, new UpdatePlanRequestDto
        {
            Name = "Profissional",
            Amount = 79.90m,
            Description = "Plano avançado",
            TrialDays = 20,
            Benefits = ["Novo benefício"]
        });

        result.Should().NotBeNull();
        result!.TrialDays.Should().Be(20);
        result.Benefits.Should().Equal("Novo benefício");
    }

    [Fact]
    public async Task GetPublicOfferAsync_ShouldExposeOnlyActivePlansWithoutInternalStatus()
    {
        _db.Plans.AddRange(
            new Plan
            {
                Name = "Ativo",
                Amount = 19.90m,
                Description = "Disponível",
                IsActive = true,
                TrialDays = 15,
                BenefitsJson = PlanBenefits.Serialize(["Benefício"])
            },
            new Plan
            {
                Name = "Inativo",
                Amount = 9.90m,
                Description = "Oculto",
                IsActive = false
            });
        await _db.SaveChangesAsync();

        var offer = await _sut.GetPublicOfferAsync();

        offer.Plans.Should().ContainSingle();
        offer.Plans[0].Name.Should().Be("Ativo");
        offer.Plans[0].TrialDays.Should().Be(15);
        offer.Plans[0].Benefits.Should().Equal("Benefício");
    }
}
