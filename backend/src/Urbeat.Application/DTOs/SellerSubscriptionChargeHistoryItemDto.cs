using Urbeat.Domain.Entities;

namespace Urbeat.Application.DTOs;

public sealed class SellerSubscriptionChargeHistoryItemDto
{
    public string GatewayChargeId { get; init; } = string.Empty;

    public string GatewayStatus { get; init; } = string.Empty;

    public SellerSubscriptionBillingStatus BillingStatus { get; init; }

    public DateTime DueDateUtc { get; init; }

    public DateTime? PaidAtUtc { get; init; }

    public decimal? Amount { get; init; }

    public string? ExternalReference { get; init; }
}