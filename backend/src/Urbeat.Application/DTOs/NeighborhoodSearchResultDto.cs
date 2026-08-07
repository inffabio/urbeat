namespace Urbeat.Application.DTOs;

public sealed class NeighborhoodSearchResultDto
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public double? Latitude { get; init; }

    public double? Longitude { get; init; }

    public bool IsActive { get; init; }

    public NeighborhoodFreightInfoDto? FreightRate { get; init; }
}
