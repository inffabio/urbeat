namespace Urbeat.Domain.Entities;

public sealed class StoreDeliveryArea : BaseEntity
{
    public Guid StoreId { get; set; }

    /// <summary>
    /// Textual neighborhood kept during the transition (Phase A of the migration). New writes
    /// also persist <see cref="NeighborhoodId"/> when the neighborhood can be resolved, and new
    /// readers prefer the identifier.
    /// </summary>
    public string Neighborhood { get; set; } = string.Empty;

    /// <summary>
    /// Resolved <see cref="DeliveryNeighborhood"/> identifier. Null while a manual/legacy area has
    /// no matching global neighborhood; those rows are reported instead of being discarded.
    /// </summary>
    public Guid? NeighborhoodId { get; set; }

    public DeliveryNeighborhood? NeighborhoodEntity { get; set; }

    public decimal DeliveryFee { get; set; }

    /// <summary>Straight-line Haversine distance (km) recorded when the area was linked.</summary>
    public double? DistanceKm { get; set; }

    /// <summary>Documents that <see cref="DistanceKm"/> is a straight-line distance, not a route.</summary>
    public string DistanceMethod { get; set; } = "haversine";

    public bool IsActive { get; set; } = true;

    public string Notes { get; set; } = string.Empty;
}
