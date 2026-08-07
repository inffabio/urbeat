namespace Urbeat.Domain.Entities;

public sealed class SellerSubscription : BaseEntity
{
    public Guid StoreId { get; set; }

    public Guid SellerUserId { get; set; }

    public Guid? PlanId { get; set; }

    public string PlanName { get; set; } = string.Empty;

    public decimal PlanAmount { get; set; }

    public SellerSubscriptionBillingStatus Status { get; set; }

    public DateTime StartDateUtc { get; set; }

    public DateTime? EndDateUtc { get; set; }

    public DateTime NextBillingDateUtc { get; set; }

    public string GatewayCustomerId { get; set; } = string.Empty;

    public string GatewaySubscriptionId { get; set; } = string.Empty;
}