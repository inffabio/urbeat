using Urbeat.Domain.Entities;

namespace Urbeat.Application.DTOs;

public sealed class SellerSubscriptionChargeHistoryItemDto
{
    public string GatewayChargeId { get; init; } = string.Empty;

    public string GatewayStatus { get; init; } = string.Empty;

    public SellerSubscriptionBillingStatus BillingStatus { get; init; }

    public DateTime DueDateUtc { get; init; }

    public DateTime? BillingPeriodStartUtc { get; init; }

    public DateTime? BillingPeriodEndUtc { get; init; }

    public DateTime? PaidAtUtc { get; init; }

    public decimal? Amount { get; init; }

    public string? ExternalReference { get; init; }

    /// <summary>
    /// Verdadeiro para cobrança stub criada localmente aguardando integração de
    /// gateway. Nesse estado não há boleto, PDF, linha digitável ou pagamento.
    /// </summary>
    public bool IsGatewayPending { get; init; }

    /// <summary>Data de criação em UTC, usada apenas para ordenação estável do histórico.</summary>
    public DateTime CreatedAtUtc { get; init; }
}