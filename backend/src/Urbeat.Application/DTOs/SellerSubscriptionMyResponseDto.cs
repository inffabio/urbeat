using Urbeat.Domain.Entities;

namespace Urbeat.Application.DTOs;

public sealed class SellerSubscriptionMyResponseDto
{
    public bool HasSubscription { get; init; }

    public string? PlanName { get; init; }

    public decimal? PlanAmount { get; init; }

    public SellerSubscriptionBillingStatus? BillingStatus { get; init; }

    public DateTime? NextDueDateUtc { get; init; }

    public string LastChargeStatus { get; init; } = "Nao contratado";

    public bool StoreBlocked { get; init; }

    public string RegularizationMessage { get; init; } = string.Empty;
}
