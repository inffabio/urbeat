namespace Urbeat.Domain.Entities;

/// <summary>
/// Histórico imutável de troca de plano. A assinatura atual é única por loja,
/// então cada upgrade registra aqui o snapshot anterior antes de aplicar as
/// novas condições comerciais.
/// </summary>
public sealed class SellerSubscriptionChange : BaseEntity
{
    public Guid SubscriptionId { get; set; }

    public Guid SellerUserId { get; set; }

    public Guid? PreviousPlanId { get; set; }

    public string PreviousPlanName { get; set; } = string.Empty;

    public decimal PreviousPlanAmount { get; set; }

    public SubscriptionBillingPeriod PreviousBillingPeriod { get; set; }

    public int PreviousPlanLevel { get; set; }

    public Guid NewPlanId { get; set; }

    public string NewPlanName { get; set; } = string.Empty;

    public decimal NewPlanAmount { get; set; }

    public SubscriptionBillingPeriod NewBillingPeriod { get; set; }

    public int NewPlanLevel { get; set; }

    public DateTime ChangedAtUtc { get; set; }

    public Guid ChangedByUserId { get; set; }

    public string Reason { get; set; } = "Upgrade";
}
