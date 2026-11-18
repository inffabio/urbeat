using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Urbeat.Application.DTOs;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Persistence.UnitOfWork;
using Urbeat.Infrastructure.Services;
using Urbeat.IntegrationTests.Infrastructure;
using Xunit.Abstractions;
using Microsoft.AspNetCore.Identity;

namespace Urbeat.IntegrationTests.Relational;

/// <summary>
/// Exercita as garantias de concorrência que só o PostgreSQL impõe: locks
/// transacionais, índices únicos e rollback de operações concorrentes. Os testes
/// são ignorados automaticamente quando Docker não está disponível.
/// </summary>
public sealed class SubscriptionConcurrencyRelationalTests
{
    private readonly ITestOutputHelper _output;

    public SubscriptionConcurrencyRelationalTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task ContractAsync_ConcurrentInitialContracts_ShouldCreateOneSubscriptionAndStub()
    {
        var container = await StartPostgresOrSkipAsync();
        if (container is null) return;

        try
        {
            var options = CreateOptions(container);
            var seed = await SeedAsync(options);

            var results = await Task.WhenAll(
                ContractAsync(options, seed.SellerUserId, seed.StoreId, seed.PlanId),
                ContractAsync(options, seed.SellerUserId, seed.StoreId, seed.PlanId));

            results.Count(x => x.Subscription is not null).Should().Be(1);
            results.Count(x => x.AlreadyContracted).Should().Be(1);

            await using var verify = new ApplicationDbContext(options);
            (await verify.SellerSubscriptions.CountAsync(x => x.StoreId == seed.StoreId)).Should().Be(1);
            (await verify.SellerSubscriptionCharges.CountAsync(x => x.StoreId == seed.StoreId)).Should().Be(1);
            (await verify.SellerSubscriptionChanges.CountAsync()).Should().Be(0);
        }
        finally
        {
            await container.DisposeAsync();
        }
    }

    [Fact]
    public async Task ContractAsync_ConcurrentUpgrades_ShouldCreateOneChangeAndOneStub()
    {
        var container = await StartPostgresOrSkipAsync();
        if (container is null) return;

        try
        {
            var options = CreateOptions(container);
            var seed = await SeedAsync(options);

            await using (var setup = new ApplicationDbContext(options))
            {
                setup.SellerSubscriptions.Add(new SellerSubscription
                {
                    StoreId = seed.StoreId,
                    SellerUserId = seed.SellerUserId,
                    PlanId = seed.PlanId,
                    PlanName = "Plano Base",
                    PlanAmount = 19.90m,
                    PlanLevelSnapshot = 0,
                    BillingPeriod = SubscriptionBillingPeriod.Monthly,
                    Status = SellerSubscriptionBillingStatus.Active,
                    StartDateUtc = DateTime.UtcNow.AddDays(-30),
                    NextBillingDateUtc = DateTime.UtcNow.AddDays(2)
                });
                await setup.SaveChangesAsync();
            }

            var results = await Task.WhenAll(
                ContractAsync(options, seed.SellerUserId, seed.StoreId, seed.SuperiorPlanId),
                ContractAsync(options, seed.SellerUserId, seed.StoreId, seed.SuperiorPlanId));

            results.Count(x => x.Subscription is not null).Should().Be(1);
            results.Count(x => x.AlreadyContracted).Should().Be(1);

            await using var verify = new ApplicationDbContext(options);
            (await verify.SellerSubscriptions.CountAsync(x => x.StoreId == seed.StoreId)).Should().Be(1);
            (await verify.SellerSubscriptionChanges.CountAsync()).Should().Be(1);
            (await verify.SellerSubscriptionCharges.CountAsync(x => x.StoreId == seed.StoreId)).Should().Be(1);
        }
        finally
        {
            await container.DisposeAsync();
        }
    }

    [Fact]
    public async Task GatewayUpsert_ConcurrentWrites_ShouldKeepOneRowWithoutDuplication()
    {
        var container = await StartPostgresOrSkipAsync();
        if (container is null) return;

        try
        {
            var options = CreateOptions(container);
            var seed = await SeedAsync(options);

            var results = await Task.WhenAll(
                UpdateGatewayAsync(options, seed.PlanId, "vault://urbeat/plan-a/api-key"),
                UpdateGatewayAsync(options, seed.PlanId, "vault://urbeat/plan-b/api-key"));

            results.Should().NotContainNulls();

            await using var verify = new ApplicationDbContext(options);
            var configs = await verify.SubscriptionGatewayConfigurations
                .Where(x => x.PlanId == seed.PlanId)
                .ToListAsync();
            configs.Should().ContainSingle();
            new[]
            {
                "vault://urbeat/plan-a/api-key",
                "vault://urbeat/plan-b/api-key"
            }.Should().Contain(configs[0].ApiKeySecretReference);
        }
        finally
        {
            await container.DisposeAsync();
        }
    }

    private static DbContextOptions<ApplicationDbContext> CreateOptions(PostgreSqlContainer container)
        => new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(container.GetConnectionString())
            .Options;

    private static async Task<ContractSellerSubscriptionResultDto> ContractAsync(
        DbContextOptions<ApplicationDbContext> options,
        Guid sellerUserId,
        Guid storeId,
        Guid planId)
    {
        await using var context = new ApplicationDbContext(options);
        var service = new SellerSubscriptionStatusService(
            context,
            new EfUnitOfWork(context),
            Moq.Mock.Of<IAsaasSubscriptionAdapter>());

        return await service.ContractAsync(
            sellerUserId,
            new ContractSellerSubscriptionRequestDto
            {
                StoreId = storeId,
                PlanId = planId,
                BillingPeriod = SubscriptionBillingPeriod.Monthly
            },
            ipAddress: null);
    }

    private static async Task<GatewayConfigurationResponseDto?> UpdateGatewayAsync(
        DbContextOptions<ApplicationDbContext> options,
        Guid planId,
        string reference)
    {
        await using var context = new ApplicationDbContext(options);
        var service = new PlanService(context, new EfUnitOfWork(context));
        return await service.UpdateGatewayConfigurationAsync(planId, new UpdateGatewayConfigurationRequestDto
        {
            Provider = "Asaas",
            Environment = "Sandbox",
            ApiKeySecretReference = reference,
            IsEnabled = true
        });
    }

    private static async Task<(Guid SellerUserId, Guid StoreId, Guid PlanId, Guid SuperiorPlanId)> SeedAsync(
        DbContextOptions<ApplicationDbContext> options)
    {
        await using var setup = new ApplicationDbContext(options);
        await setup.Database.EnsureCreatedAsync();

        var sellerUserId = Guid.NewGuid();
        setup.Users.Add(new IdentityUser<Guid>
        {
            Id = sellerUserId,
            UserName = $"concurrency-{sellerUserId:N}@urbeat.local",
            Email = $"concurrency-{sellerUserId:N}@urbeat.local"
        });

        var store = new Store
        {
            OwnerUserId = sellerUserId,
            Name = "Loja Concorrência",
            Slug = $"loja-concorrencia-{sellerUserId:N}",
            PhoneNumber = "11999999999"
        };
        setup.Stores.Add(store);

        var plan = new Plan
        {
            Name = $"Plano Base {Guid.NewGuid():N}",
            Amount = 19.90m,
            Description = "Base",
            IsActive = true,
            Level = 0,
            MonthlyEnabled = true
        };
        var superior = new Plan
        {
            Name = $"Plano Superior {Guid.NewGuid():N}",
            Amount = 49.90m,
            Description = "Superior",
            IsActive = true,
            Level = 2,
            MonthlyEnabled = true
        };
        setup.Plans.AddRange(plan, superior);
        await setup.SaveChangesAsync();

        return (sellerUserId, store.Id, plan.Id, superior.Id);
    }

    private async Task<PostgreSqlContainer?> StartPostgresOrSkipAsync()
    {
        PostgreSqlContainer? container = null;

        try
        {
            container = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .WithDatabase("urbeat")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();

            await container.StartAsync();
            return container;
        }
        catch (Exception exception)
        {
            if (container is not null)
            {
                await container.DisposeAsync();
            }

            _output.WriteLine($"SKIPPED (Docker unavailable): {exception.Message}");
            return null;
        }
    }
}
