using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Persistence.UnitOfWork;
using Urbeat.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using Npgsql;

namespace Urbeat.UnitTests.Infrastructure.Services;

public sealed class ConfigurablePlanServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly PlanService _sut;

    public ConfigurablePlanServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-configurable-plan-{Guid.NewGuid()}")
            .Options;
        _db = new ApplicationDbContext(options);
        _sut = new PlanService(_db, new EfUnitOfWork(_db));
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task CreateAsync_ShouldPersistBillingPeriodsDiscountAndLevel()
    {
        var result = await _sut.CreateAsync(new CreatePlanRequestDto
        {
            Name = "Crescer",
            MonthlyAmount = 39.90m,
            AnnualEnabled = true,
            AnnualDiscountPercent = 20,
            Level = 2,
            TrialDays = 10,
            Benefits = ["Relatórios"]
        });

        result.Should().NotBeNull();
        result!.MonthlyAmount.Should().Be(39.90m);
        result.Amount.Should().Be(39.90m);
        result.AnnualAmount.Should().Be(383.04m);
        result.AnnualDiscountPercent.Should().Be(20);
        result.MonthlyEnabled.Should().BeTrue();
        result.AnnualEnabled.Should().BeTrue();
        result.Level.Should().Be(2);

        var persisted = await _db.Plans.SingleAsync();
        persisted.MonthlyEnabled.Should().BeTrue();
        persisted.AnnualEnabled.Should().BeTrue();
        persisted.AnnualDiscountPercent.Should().Be(20);
        persisted.Level.Should().Be(2);
    }

    [Fact]
    public async Task CreateAsync_ShouldPreferMonthlyAmountOverLegacyAmount()
    {
        var result = await _sut.CreateAsync(new CreatePlanRequestDto
        {
            Name = "Preço",
            Amount = 99.90m,
            MonthlyAmount = 49.90m
        });

        result!.MonthlyAmount.Should().Be(49.90m);
        (await _db.Plans.SingleAsync()).Amount.Should().Be(49.90m);
    }

    [Fact]
    public async Task UpdateAsync_ShouldPreserveOmittedPricingAndAllowLevelOrdering()
    {
        var plan = new Plan
        {
            Name = "Crescer",
            Amount = 39.90m,
            Description = "Plano",
            MonthlyEnabled = true,
            AnnualEnabled = true,
            AnnualDiscountPercent = 10,
            Level = 1
        };
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();

        var result = await _sut.UpdateAsync(plan.Id, new UpdatePlanRequestDto
        {
            Name = "Crescer",
            Description = "Plano",
            Level = 3
        });

        result.Should().NotBeNull();
        result!.MonthlyAmount.Should().Be(39.90m);
        result.AnnualEnabled.Should().BeTrue();
        result.AnnualDiscountPercent.Should().Be(10);
        result.Level.Should().Be(3);
    }

    [Fact]
    public async Task GetPublicOfferAsync_ShouldOrderByLevelAndExposeOnlySafeFields()
    {
        _db.Plans.AddRange(
            new Plan { Name = "Alto", Amount = 10m, Level = 2, IsActive = true, AnnualEnabled = true, AnnualDiscountPercent = 10 },
            new Plan { Name = "Baixo", Amount = 90m, Level = 1, IsActive = true },
            new Plan { Name = "Inativo", Amount = 5m, Level = 3, IsActive = false },
            new Plan { Name = "Sem período", Amount = 5m, Level = 4, IsActive = true, MonthlyEnabled = false, AnnualEnabled = false });
        await _db.SaveChangesAsync();

        var offer = await _sut.GetPublicOfferAsync();

        offer.Plans.Select(x => x.Name).Should().Equal("Baixo", "Alto");
        offer.Plans[1].AnnualEnabled.Should().BeTrue();
        offer.Plans[1].AnnualDiscountPercent.Should().Be(10);
        offer.Plans[1].AnnualAmount.Should().Be(108.00m);
        offer.Plans.Should().OnlyContain(plan => plan.MonthlyAmount > 0);
    }

    [Fact]
    public async Task DeactivatingPlan_ShouldNotMutateExistingSubscriptionSnapshots()
    {
        var plan = new Plan { Name = "Crescer", Amount = 39.90m, IsActive = true };
        _db.Plans.Add(plan);
        var subscription = new SellerSubscription
        {
            StoreId = Guid.NewGuid(),
            SellerUserId = Guid.NewGuid(),
            PlanId = plan.Id,
            PlanName = "Crescer",
            PlanAmount = 39.90m,
            PlanDescriptionSnapshot = "Descrição contratada",
            TrialDaysSnapshot = 12,
            BillingPeriod = SubscriptionBillingPeriod.Monthly,
            StartDateUtc = DateTime.UtcNow,
            NextBillingDateUtc = DateTime.UtcNow.AddDays(12)
        };
        _db.SellerSubscriptions.Add(subscription);
        await _db.SaveChangesAsync();

        await _sut.UpdateStatusAsync(plan.Id, isActive: false);

        var persisted = await _db.SellerSubscriptions.SingleAsync();
        persisted.PlanName.Should().Be("Crescer");
        persisted.PlanAmount.Should().Be(39.90m);
        persisted.PlanDescriptionSnapshot.Should().Be("Descrição contratada");
        persisted.TrialDaysSnapshot.Should().Be(12);
    }

    [Fact]
    public async Task GatewayConfiguration_ShouldBeIndependentPerPlanAndNeverReturnSecrets()
    {
        var firstPlan = new Plan { Name = "Plano A", Amount = 19.90m, IsActive = true };
        var secondPlan = new Plan { Name = "Plano B", Amount = 39.90m, IsActive = true };
        _db.Plans.AddRange(firstPlan, secondPlan);
        await _db.SaveChangesAsync();

        var first = await _sut.UpdateGatewayConfigurationAsync(firstPlan.Id, new UpdateGatewayConfigurationRequestDto
        {
            Provider = "Asaas",
            Environment = "Sandbox",
            ApiBaseUrl = "https://sandbox.asaas.com",
            WebhookUrl = "https://urbeat.test/webhooks/subscription/plan-a",
            ApiKeySecretReference = "vault://subscription/plan-a/api-key",
            WebhookSecretReference = "vault://subscription/plan-a/webhook",
            IsEnabled = true
        });

        var second = await _sut.UpdateGatewayConfigurationAsync(secondPlan.Id, new UpdateGatewayConfigurationRequestDto
        {
            Provider = "Asaas",
            Environment = "Production",
            ApiBaseUrl = "https://api.asaas.com",
            WebhookUrl = "https://urbeat.test/webhooks/subscription/plan-b",
            ApiKeySecretReference = "vault://subscription/plan-b/api-key",
            WebhookSecretReference = "vault://subscription/plan-b/webhook",
            IsEnabled = false
        });

        first.Should().NotBeNull();
        second.Should().NotBeNull();
        first!.PlanId.Should().Be(firstPlan.Id);
        second!.PlanId.Should().Be(secondPlan.Id);
        first.ApiBaseUrl.Should().Be("https://sandbox.asaas.com");
        second.ApiBaseUrl.Should().Be("https://api.asaas.com");
        first.HasApiKey.Should().BeTrue();
        second.HasApiKey.Should().BeTrue();
        first.HasWebhookSecret.Should().BeTrue();
        second.HasWebhookSecret.Should().BeTrue();

        var persisted = await _db.SubscriptionGatewayConfigurations
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync();
        persisted.Should().HaveCount(2);
        persisted.Should().OnlyContain(x => x.PlanId != Guid.Empty);
        persisted.Single(x => x.PlanId == firstPlan.Id).ApiKeySecretReference.Should().Be("vault://subscription/plan-a/api-key");
        persisted.Single(x => x.PlanId == secondPlan.Id).ApiKeySecretReference.Should().Be("vault://subscription/plan-b/api-key");

        var firstRead = await _sut.GetGatewayConfigurationAsync(firstPlan.Id);
        var secondRead = await _sut.GetGatewayConfigurationAsync(secondPlan.Id);
        firstRead!.PlanId.Should().Be(firstPlan.Id);
        secondRead!.PlanId.Should().Be(secondPlan.Id);
        firstRead.Environment.Should().Be("Sandbox");
        secondRead.Environment.Should().Be("Production");
        firstRead.HasApiKey.Should().BeTrue();
        firstRead.HasWebhookSecret.Should().BeTrue();
    }

    [Fact]
    public async Task GatewayConfiguration_ShouldPreserveOmittedSecretsAndAllowClearingPerPlan()
    {
        var plan = new Plan { Name = "Plano Gateway", Amount = 19.90m, IsActive = true };
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();

        await _sut.UpdateGatewayConfigurationAsync(plan.Id, new UpdateGatewayConfigurationRequestDto
        {
            Provider = "Asaas",
            ApiKeySecretReference = "vault://subscription/secret/api-key",
            WebhookSecretReference = "vault://subscription/secret/webhook"
        });

        await _sut.UpdateGatewayConfigurationAsync(plan.Id, new UpdateGatewayConfigurationRequestDto
        {
            Provider = "Asaas",
            Environment = "Production",
            ApiBaseUrl = "https://api.asaas.com",
            WebhookUrl = string.Empty,
            ApiKeySecretReference = null,
            WebhookSecretReference = null,
            IsEnabled = false
        });

        var preserved = await _db.SubscriptionGatewayConfigurations.SingleAsync(x => x.PlanId == plan.Id);
        preserved.ApiKeySecretReference.Should().Be("vault://subscription/secret/api-key");
        preserved.WebhookSecretReference.Should().Be("vault://subscription/secret/webhook");
        preserved.Environment.Should().Be("Production");
        preserved.IsEnabled.Should().BeFalse();

        var response = await _sut.GetGatewayConfigurationAsync(plan.Id);
        response!.HasApiKey.Should().BeTrue();
        response.HasWebhookSecret.Should().BeTrue();

        await _sut.UpdateGatewayConfigurationAsync(plan.Id, new UpdateGatewayConfigurationRequestDto
        {
            Provider = "Asaas",
            ApiKeySecretReference = string.Empty,
            WebhookSecretReference = " "
        });

        var cleared = await _sut.GetGatewayConfigurationAsync(plan.Id);
        cleared!.HasApiKey.Should().BeFalse();
        cleared.HasWebhookSecret.Should().BeFalse();
    }

    [Fact]
    public async Task GatewayConfiguration_ShouldRetryDeterministically_WhenConcurrentInsertHitsUniqueConstraint()
    {
        var plan = new Plan { Name = "Plano Gateway Concorrente", Amount = 19.90m, IsActive = true };
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();

        var uow = new Mock<IEfUnitOfWork>();
        var calls = 0;
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                calls++;
                if (calls == 1)
                {
                    throw new DbUpdateException(
                        "concurrent insert",
                        new PostgresException(
                            "duplicate key value violates unique constraint \"IX_SubscriptionGatewayConfigurations_PlanId\"",
                            "ERROR",
                            "ERROR",
                            PostgresErrorCodes.UniqueViolation));
                }

                return _db.SaveChangesAsync();
            });

        var sut = new PlanService(_db, uow.Object);

        var result = await sut.UpdateGatewayConfigurationAsync(plan.Id, new UpdateGatewayConfigurationRequestDto
        {
            Provider = "Asaas",
            Environment = "Sandbox",
            ApiKeySecretReference = "vault://urbeat/concurrent/api-key",
            IsEnabled = true
        });

        result.Should().NotBeNull();
        result!.PlanId.Should().Be(plan.Id);
        calls.Should().Be(2);
        var persisted = await _db.SubscriptionGatewayConfigurations.SingleAsync(x => x.PlanId == plan.Id);
        persisted.ApiKeySecretReference.Should().Be("vault://urbeat/concurrent/api-key");
    }

    [Fact]
    public async Task GatewayConfiguration_ShouldReturnNullForUnknownPlan()
    {
        var result = await _sut.GetGatewayConfigurationAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    private sealed class FakeEncryptionService : IEncryptionService
    {
        public string Encrypt(string plainText) => $"enc:{plainText}";

        public string Decrypt(string cipherText) => cipherText.StartsWith("enc:", StringComparison.Ordinal)
            ? cipherText[4..]
            : cipherText;
    }
}
