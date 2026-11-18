namespace Urbeat.Domain.Entities;

/// <summary>
/// Seleção de plano informada no cadastro do lojista. O servidor valida o
/// catálogo no momento do cadastro e consome esta seleção ao criar a loja,
/// vinculando a assinatura com snapshot das condições contratadas.
/// </summary>
public sealed class SellerPlanSelection : BaseEntity
{
    public Guid UserId { get; set; }

    public Guid PlanId { get; set; }

    public SubscriptionBillingPeriod BillingPeriod { get; set; }
}
