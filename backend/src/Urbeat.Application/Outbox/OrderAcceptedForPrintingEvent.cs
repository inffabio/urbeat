using Urbeat.Domain.Entities;

namespace Urbeat.Application.Outbox;

/// <summary>
/// Durable event emitted only when an order transitions from <see cref="OrderStatus.Received"/> to
/// <see cref="OrderStatus.Preparing"/> (the seller acceptance). It carries the stable acceptance
/// identity the dashboard/print agent need to deduplicate at-least-once deliveries.
/// </summary>
public sealed class OrderAcceptedForPrintingEvent
{
    public Guid OrderId { get; set; }

    public Guid StoreId { get; set; }

    public Guid CustomerUserId { get; set; }

    public Guid SellerUserId { get; set; }

    public string Code { get; set; } = string.Empty;

    public DateTime AcceptedAtUtc { get; set; }

    /// <summary>Per-order monotonic sequence of the acceptance transition.</summary>
    public int Sequence { get; set; }
}
