namespace Urbeat.Application.DTOs;

public sealed class DeliveryNeighborhoodResponseDto
{
    public Guid Id { get; init; }

    public string Neighborhood { get; init; } = string.Empty;

    public string NormalizedName { get; init; } = string.Empty;

    public string City { get; init; } = string.Empty;

    public Guid? CityId { get; init; }

    public string? OsmId { get; init; }

    public string? OsmType { get; init; }

    public string? PlaceType { get; init; }

    public double? Latitude { get; init; }

    public double? Longitude { get; init; }

    public string? Source { get; init; }

    public bool IsActive { get; init; }

    /// <summary><c>official_cnefe</c>, <c>manual</c> or <c>legacy</c>.</summary>
    public string Kind { get; init; } = "legacy";

    /// <summary><c>computed</c>, <c>pending</c> or <c>manual</c>.</summary>
    public string CoordinateStatus { get; init; } = "pending";

    public string? CoordinateMethod { get; init; }

    public int CoordinatePointCount { get; init; }

    public string? DatasetVersion { get; init; }

    /// <summary>Straight-line Haversine distance from the store, rounded for display.</summary>
    public double? DistanceKm { get; init; }

    /// <summary>True only when <see cref="DistanceKm"/> is inside the applied radius.</summary>
    public bool? Eligible { get; init; }

    /// <summary>Manual neighborhoods have no coordinate and need an explicit seller decision.</summary>
    public bool RequiresManualReview { get; init; }
}
