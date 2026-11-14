namespace Urbeat.Domain.Entities;

/// <summary>
/// Geographic street/logradouro derived from the CNEFE artifact. The artifact is pre-aggregated
/// per logradouro, so one row groups the source points of the same
/// <c>(municipality, neighborhood, logradouro)</c> and keeps the mean coordinate of its points.
/// Individual address points are not duplicated in PostgreSQL: the validated neighborhood centroid
/// is the authoritative value and the raw SQLite artifact remains the audit source.
/// </summary>
public sealed class DeliveryStreet : BaseEntity
{
    public Guid CityId { get; set; }

    public City? CityEntity { get; set; }

    /// <summary>
    /// Null when the source locality was empty or could not be resolved. Such street rows are
    /// published as pending and counted in the import report; no synthetic neighborhood is created.
    /// </summary>
    public Guid? NeighborhoodId { get; set; }

    public DeliveryNeighborhood? NeighborhoodEntity { get; set; }

    /// <summary>Display name, preserving the source spelling.</summary>
    public string Name { get; set; } = string.Empty;

    public string NormalizedName { get; set; } = string.Empty;

    /// <summary>Always null in the current artifact (no street type column exists).</summary>
    public string? StreetType { get; set; }

    /// <summary>Always null in the current artifact (no street title column exists).</summary>
    public string? StreetTitle { get; set; }

    /// <summary>Mean latitude of the valid source points of this logradouro.</summary>
    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public string? CoordinateMethod { get; set; }

    /// <summary>Number of valid source points aggregated into this street row.</summary>
    public int AddressPointCount { get; set; }

    public string Source { get; set; } = "cnefe";

    public string SourceVersion { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}
