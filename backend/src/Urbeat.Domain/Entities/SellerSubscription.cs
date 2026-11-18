namespace Urbeat.Domain.Entities;

public sealed class SellerSubscription : BaseEntity
{
    public Guid StoreId { get; set; }

    public Guid SellerUserId { get; set; }

    public Guid? PlanId { get; set; }

    public string PlanName { get; set; } = string.Empty;

    public decimal PlanAmount { get; set; }

    public SellerSubscriptionBillingStatus Status { get; set; }

    public DateTime StartDateUtc { get; set; }

    public DateTime? EndDateUtc { get; set; }

    public DateTime NextBillingDateUtc { get; set; }

    public string GatewayCustomerId { get; set; } = string.Empty;

    public string GatewaySubscriptionId { get; set; } = string.Empty;

    /// <summary>Periodicidade contratada; define o valor e a janela da cobrança.</summary>
    public SubscriptionBillingPeriod BillingPeriod { get; set; } = SubscriptionBillingPeriod.Monthly;

    /// <summary>Descrição do plano no momento da contratação (snapshot histórico).</summary>
    public string PlanDescriptionSnapshot { get; set; } = string.Empty;

    /// <summary>Dias de trial contratados; não é recalculado por edições posteriores do plano.</summary>
    public int TrialDaysSnapshot { get; set; }

    /// <summary>Desconto anual contratado, preservado para o histórico.</summary>
    public int AnnualDiscountPercentSnapshot { get; set; }

    /// <summary>Nível do plano no momento da contratação; base para validar upgrades.</summary>
    public int PlanLevelSnapshot { get; set; }

    /// <summary>Fim do período gratuito em UTC (snapshot). Nulo quando o plano não tem trial.</summary>
    public DateTime? TrialEndsAtUtc { get; set; }
}
