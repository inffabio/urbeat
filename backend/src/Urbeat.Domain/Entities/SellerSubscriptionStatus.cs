namespace Urbeat.Domain.Entities;

public sealed class SellerSubscriptionStatus : BaseEntity
{
    public Guid SellerUserId { get; set; }

    public DateTime NextDueDateUtc { get; set; }

    public SellerSubscriptionBillingStatus BillingStatus { get; set; }

    public DateTime? LastNotifiedAtUtc { get; set; }
}
