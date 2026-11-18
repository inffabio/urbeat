using Urbeat.Domain.Entities;

namespace Urbeat.Application.DTOs;

public sealed class ContractSellerSubscriptionResponseDto
{
    public Guid SubscriptionId { get; init; }

    public Guid StoreId { get; init; }

    public Guid SellerUserId { get; init; }

    public string PlanName { get; init; } = string.Empty;

    public string PlanDescription { get; init; } = string.Empty;

    public decimal PlanAmount { get; init; }

    public SubscriptionBillingPeriod BillingPeriod { get; init; }

    public SellerSubscriptionBillingStatus Status { get; init; }

    public DateTime StartDateUtc { get; init; }

    public DateTime? TrialEndsAtUtc { get; init; }

    public DateTime NextBillingDateUtc { get; init; }

    /// <summary>
    /// Identificadores legados do gateway. A nova contratação local não gera
    /// identificadores; permanecem vazios até a integração futura.
    /// </summary>
    public string GatewayCustomerId { get; init; } = string.Empty;

    public string GatewaySubscriptionId { get; init; } = string.Empty;

    /// <summary>Cobrança criada como stub aguardando integração de gateway.</summary>
    public bool AwaitingGateway { get; init; }
}
