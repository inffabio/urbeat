using Urbeat.Domain.Entities;

namespace Urbeat.Application.DTOs;

public sealed class UpsertSellerSubscriptionStatusRequestDto
{
    public Guid SellerUserId { get; init; }

    public DateTime NextDueDateUtc { get; init; }

    public SellerSubscriptionBillingStatus BillingStatus { get; init; }
}
