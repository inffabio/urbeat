namespace Urbeat.Application.DTOs;

public sealed class CreateDeliveryNeighborhoodResultDto
{
    public bool NotFound { get; init; }

    public bool Forbidden { get; init; }

    public bool CityRequired { get; init; }

    public bool Conflict { get; init; }

    public DeliveryNeighborhoodResponseDto? Neighborhood { get; init; }
}
