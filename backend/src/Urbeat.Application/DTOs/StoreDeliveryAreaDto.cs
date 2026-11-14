namespace Urbeat.Application.DTOs;

public sealed class StoreDeliveryAreaDto
{
    public Guid Id { get; init; }
    public string Neighborhood { get; init; } = string.Empty;

    /// <summary>
    /// Resolved global neighborhood identifier. Null while a manual area has no global match; the
    /// textual <see cref="Neighborhood"/> remains the transitional fallback for those rows.
    /// </summary>
    public Guid? NeighborhoodId { get; init; }

    public decimal DeliveryFee { get; init; }
    public bool IsActive { get; init; } = true;
    public string Notes { get; init; } = string.Empty;

    /// <summary>Straight-line Haversine distance recorded when the area was linked.</summary>
    public double? DistanceKm { get; init; }

    /// <summary>Always <c>haversine</c>: the value is not a route distance.</summary>
    public string DistanceMethod { get; init; } = "haversine";
}
