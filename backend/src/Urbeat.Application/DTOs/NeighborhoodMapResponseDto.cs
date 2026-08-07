namespace Urbeat.Application.DTOs;

public sealed class NeighborhoodMapResponseDto
{
    public CityMapInfoDto City { get; init; } = new();

    public IReadOnlyCollection<NeighborhoodMapItemDto> Items { get; init; } = Array.Empty<NeighborhoodMapItemDto>();

    public IReadOnlyCollection<NeighborhoodWithoutCoordinatesDto> WithoutCoordinates { get; init; } = Array.Empty<NeighborhoodWithoutCoordinatesDto>();
}

public sealed class CityMapInfoDto
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Uf { get; init; } = string.Empty;
}

public sealed class NeighborhoodMapItemDto
{
    public Guid NeighborhoodId { get; init; }

    public string Name { get; init; } = string.Empty;

    public double? Latitude { get; init; }

    public double? Longitude { get; init; }

    public decimal Rate { get; init; }

    public decimal? MinimumOrderValue { get; init; }

    public int? EstimatedDeliveryTimeMinutes { get; init; }

    public bool Active { get; init; }
}

public sealed class NeighborhoodWithoutCoordinatesDto
{
    public Guid NeighborhoodId { get; init; }

    public string Name { get; init; } = string.Empty;
}
