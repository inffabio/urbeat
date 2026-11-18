using Urbeat.Domain.Entities;

namespace Urbeat.Application.DTOs;

public sealed class ContractSellerSubscriptionRequestDto
{
    public Guid StoreId { get; init; }

    public Guid PlanId { get; init; }

    /// <summary>
    /// Periodicidade desejada. Ausente usa mensal para compatibilidade com
    /// clientes antigos; o servidor valida se o plano oferece a periodicidade.
    /// </summary>
    public SubscriptionBillingPeriod? BillingPeriod { get; init; }

    public SubscriptionBillingPeriod EffectiveBillingPeriod => BillingPeriod ?? SubscriptionBillingPeriod.Monthly;

    /// <summary>
    /// Mantido por compatibilidade de contrato, mas ignorado pelo servidor.
    /// A primeira cobrança é calculada a partir do trial do plano selecionado.
    /// </summary>
    public DateTime FirstDueDateUtc { get; init; }
}
