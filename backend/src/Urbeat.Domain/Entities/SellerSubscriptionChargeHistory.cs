namespace Urbeat.Domain.Entities;

public sealed class SellerSubscriptionChargeHistory : BaseEntity
{
    public Guid SellerUserId { get; set; }

    public string GatewayChargeId { get; set; } = string.Empty;

    public string? ExternalReference { get; set; }

    public string GatewayStatus { get; set; } = string.Empty;

    public SellerSubscriptionBillingStatus BillingStatus { get; set; }

    public DateTime DueDateUtc { get; set; }

    public DateTime? PaidAtUtc { get; set; }

    public decimal? Amount { get; set; }

    public string RawPayload { get; set; } = string.Empty;
}