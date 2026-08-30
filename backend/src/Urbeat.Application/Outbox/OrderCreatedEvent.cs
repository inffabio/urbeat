using Urbeat.Domain.Entities;

namespace Urbeat.Application.Outbox;

public sealed class OrderCreatedEvent
{
    public Guid OrderId { get; set; }

    public Guid StoreId { get; set; }

    public Guid CustomerUserId { get; set; }

    public Guid SellerUserId { get; set; }

    public string Code { get; set; } = string.Empty;

    public OrderStatus Status { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    /// <summary>Per-order monotonic sequence (creation is 1).</summary>
    public int Sequence { get; set; } = 1;
}
