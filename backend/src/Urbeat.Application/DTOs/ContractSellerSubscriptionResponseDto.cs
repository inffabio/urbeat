using Urbeat.Domain.Entities;

namespace Urbeat.Application.DTOs;

public sealed class ContractSellerSubscriptionResponseDto
{
    public Guid SubscriptionId { get; init; }

    public Guid StoreId { get; init; }

    public Guid SellerUserId { get; init; }

    public string PlanName { get; init; } = string.Empty;

    public decimal PlanAmount { get; init; }

    public SellerSubscriptionBillingStatus Status { get; init; }

    public DateTime StartDateUtc { get; init; }

    public DateTime NextBillingDateUtc { get; init; }

    public string GatewayCustomerId { get; init; } = string.Empty;

    public string GatewaySubscriptionId { get; init; } = string.Empty;
}