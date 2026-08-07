namespace Urbeat.Application.DTOs;

public sealed class ContractSellerSubscriptionRequestDto
{
    public Guid StoreId { get; init; }

    public Guid PlanId { get; init; }

    public DateTime FirstDueDateUtc { get; init; }
}