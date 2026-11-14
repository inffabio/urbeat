namespace Urbeat.Domain.Entities;

public sealed class DeliveryNeighborhood : BaseEntity
{
    public Guid? CityId { get; set; }
    public City? CityEntity { get; set; }

    /// <summary>
    /// Textual city name kept during the transition to <see cref="CityId"/>. New readers prefer
    /// <see cref="CityId"/> and only fall back to this value for legacy rows.
    /// </summary>
    public string City { get; set; } = string.Empty;

    public string Neighborhood { get; set; } = string.Empty;

    public string NormalizedName { get; set; } = string.Empty;

    /// <summary>
    /// <c>official_cnefe</c>, <c>manual</c> or <c>legacy</c>. See
    /// <see cref="Urbeat.Domain.Geo.GeographicProvenance.NeighborhoodKind"/>.
    /// </summary>
    public string Kind { get; set; } = "legacy";

    public string? OsmId { get; set; }

    public string? OsmType { get; set; }

    public string? PlaceType { get; set; }

    public string? Boundary { get; set; }

    public string? AdminLevel { get; set; }

    /// <summary>
    /// Operational centroid used by the delivery radius. For official CNEFE neighborhoods it is
    /// the validated <c>ADDRESS_POINT_MEAN</c>; manual neighborhoods never receive one
    /// automatically.
    /// </summary>
    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    /// <summary><c>ADDRESS_POINT_MEAN</c> for official coordinates; null for legacy/manual rows.</summary>
    public string? CoordinateMethod { get; set; }

    /// <summary>Number of valid address points used to compute the centroid.</summary>
    public int CoordinatePointCount { get; set; }

    /// <summary>Dataset version/checksum that produced the coordinate.</summary>
    public string? CoordinateSourceVersion { get; set; }

    public DateTime? CoordinateCalculatedAtUtc { get; set; }

    /// <summary><c>computed</c>, <c>pending</c> or <c>manual</c>.</summary>
    public string CoordinateStatus { get; set; } = "pending";

    /// <summary>Auxiliary audit point (nearest valid address point to the centroid).</summary>
    public double? RepresentativeLatitude { get; set; }

    public double? RepresentativeLongitude { get; set; }

    public string? RepresentativeMethod { get; set; }

    /// <summary>
    /// Auxiliary classification of the locality (urban neighborhood, indigenous village, rural
    /// locality, ...). It never changes radius eligibility.
    /// </summary>
    public string? LocalityType { get; set; }

    public string? Source { get; set; }

    public bool IsActive { get; set; } = true;
}
