using Urbeat.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Urbeat.Infrastructure.Persistence;

/// <summary>
/// Cria cobranças demonstrativas idempotentes para o dashboard de Mensalidade.
/// Como o histórico de cobrança não possui StoreId, as assinaturas são
/// selecionadas de forma determinística e cada cobrança é vinculada à loja por
/// meio do <see cref="SellerSubscriptionChargeHistory.GatewayChargeId"/>.
/// Para cada uma das duas primeiras assinaturas existentes são criadas duas
/// cobranças (uma paga e uma pendente/vencida), totalizando 4 registros.
/// As datas e os IDs são fixos e claramente passados, para que o seed não
/// crie novos registros a cada mês/startup. Nunca cria lojas ou usuários.
/// </summary>
public sealed class DemoSubscriptionChargeSeeder
{
    private const int MaxEligibleStores = 2;

    private const string PaidChargeSuffix = "PAID";

    private const string OverdueChargeSuffix = "OVERDUE";

    private static readonly DateTime DemoPaidPeriodStartUtc = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime DemoOverduePeriodStartUtc = new(2023, 12, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<DemoSubscriptionChargeSeeder> _logger;

    public DemoSubscriptionChargeSeeder(
        ApplicationDbContext dbContext,
        ILogger<DemoSubscriptionChargeSeeder> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<int> SeedAsync(CancellationToken cancellationToken = default)
    {
        var subscriptions = await _dbContext.SellerSubscriptions
            .AsNoTracking()
            .OrderBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.Id)
            .Take(MaxEligibleStores)
            .Select(x => new EligibleSubscription
            {
                StoreId = x.StoreId,
                SellerUserId = x.SellerUserId,
                PlanAmount = x.PlanAmount
            })
            .ToListAsync(cancellationToken);

        if (subscriptions.Count == 0)
        {
            _logger.LogWarning(
                "DemoSubscriptionChargeSeeder: nenhuma assinatura elegível encontrada; nenhuma cobrança demonstrativa criada.");
            return 0;
        }

        if (subscriptions.Count < MaxEligibleStores)
        {
            _logger.LogWarning(
                "DemoSubscriptionChargeSeeder: apenas {Count} assinatura(s) elegível(is); seedando as disponíveis.",
                subscriptions.Count);
        }

        var created = 0;

        foreach (var subscription in subscriptions)
        {
            var externalReference = subscription.SellerUserId.ToString();

            created += await AddChargeIfMissingAsync(
                subscription,
                BuildDemoChargeId(subscription.StoreId, PaidChargeSuffix),
                SellerSubscriptionBillingStatus.Active,
                "RECEIVED",
                DemoPaidPeriodStartUtc,
                DemoPaidPeriodStartUtc.AddMonths(1),
                DemoPaidPeriodStartUtc,
                externalReference,
                cancellationToken);

            created += await AddChargeIfMissingAsync(
                subscription,
                BuildDemoChargeId(subscription.StoreId, OverdueChargeSuffix),
                SellerSubscriptionBillingStatus.Overdue,
                "OVERDUE",
                DemoOverduePeriodStartUtc,
                DemoOverduePeriodStartUtc.AddMonths(1),
                null,
                externalReference,
                cancellationToken);
        }

        if (created > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "DemoSubscriptionChargeSeeder: {Count} cobrança(s) demonstrativa(s) criada(s).",
                created);
        }

        return created;
    }

    private async Task<int> AddChargeIfMissingAsync(
        EligibleSubscription subscription,
        string gatewayChargeId,
        SellerSubscriptionBillingStatus billingStatus,
        string gatewayStatus,
        DateTime periodStartUtc,
        DateTime periodEndUtc,
        DateTime? paidAtUtc,
        string externalReference,
        CancellationToken cancellationToken)
    {
        var exists = await _dbContext.SellerSubscriptionChargeHistories
            .AsNoTracking()
            .AnyAsync(x => x.GatewayChargeId == gatewayChargeId, cancellationToken);

        if (exists)
        {
            return 0;
        }

        await _dbContext.SellerSubscriptionChargeHistories.AddAsync(new SellerSubscriptionChargeHistory
        {
            SellerUserId = subscription.SellerUserId,
            GatewayChargeId = gatewayChargeId,
            ExternalReference = externalReference,
            GatewayStatus = gatewayStatus,
            BillingStatus = billingStatus,
            DueDateUtc = periodStartUtc,
            BillingPeriodStartUtc = periodStartUtc,
            BillingPeriodEndUtc = periodEndUtc,
            PaidAtUtc = paidAtUtc,
            Amount = subscription.PlanAmount,
            RawPayload = "{}"
        }, cancellationToken);

        return 1;
    }

    private static string BuildDemoChargeId(Guid storeId, string suffix)
    {
        return $"DEMO-BASIC-{storeId:N}-{suffix}";
    }

    private sealed class EligibleSubscription
    {
        public Guid StoreId { get; init; }

        public Guid SellerUserId { get; init; }

        public decimal PlanAmount { get; init; }
    }
}
