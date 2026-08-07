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
}
