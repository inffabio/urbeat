namespace Urbeat.Application.DTOs;

public sealed class UpdateNeighborhoodRequestDto
{
    public string? Name { get; init; }

    public double? Latitude { get; init; }

    public double? Longitude { get; init; }

    public bool? IsActive { get; init; }
}
