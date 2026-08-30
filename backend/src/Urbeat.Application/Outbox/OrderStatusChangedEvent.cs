using Urbeat.Domain.Entities;

namespace Urbeat.Application.Outbox;

public sealed class OrderStatusChangedEvent
{
    public Guid OrderId { get; set; }

    public Guid StoreId { get; set; }

    public Guid CustomerUserId { get; set; }

    public Guid SellerUserId { get; set; }

    public string Code { get; set; } = string.Empty;

    public OrderStatus PreviousStatus { get; set; }

    public OrderStatus NewStatus { get; set; }

    public DateTime ChangedAtUtc { get; set; }

    public string Source { get; set; } = "Seller";

    /// <summary>Per-order monotonic sequence (creation is 1; each transition increments).</summary>
    public int Sequence { get; set; }
}
