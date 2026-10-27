namespace Urbeat.Domain.Entities;

/// <summary>
/// Ordered stop of a <see cref="DeliveryRoute"/>. The sequence is defined by the seller and is
/// never optimized externally. A stop always points to an order of the same store and to a
/// driver assignment compatible with the route.
/// </summary>
public sealed class DeliveryRouteOrder : BaseEntity
{
    public Guid DeliveryRouteId { get; set; }

    public DeliveryRoute? DeliveryRoute { get; set; }

    public Guid OrderId { get; set; }

    public Order? Order { get; set; }

    public int Sequence { get; set; }
}
