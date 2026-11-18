namespace Urbeat.Domain.Entities;

/// <summary>
/// Cobrança local de assinatura. Nesta versão representa somente o stub
/// <see cref="SellerSubscriptionChargeStatus.AwaitingGateway"/>; não há
/// identificador, código de boleto, PDF ou URL de pagamento.
/// </summary>
public sealed class SellerSubscriptionCharge : BaseEntity
{
    public Guid SellerUserId { get; set; }

    public Guid StoreId { get; set; }

    public Guid? SubscriptionId { get; set; }

    public Guid? PlanId { get; set; }

    public string PlanNameSnapshot { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public SubscriptionBillingPeriod BillingPeriod { get; set; }

    public SellerSubscriptionChargeStatus Status { get; set; } = SellerSubscriptionChargeStatus.AwaitingGateway;

    public DateTime DueDateUtc { get; set; }

    public DateTime BillingPeriodStartUtc { get; set; }

    public DateTime BillingPeriodEndUtc { get; set; }

    /// <summary>
    /// Chave idempotente da criação (por exemplo, a primeira cobrança de uma
    /// loja). Impede duplicidade quando a operação é repetida.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}
