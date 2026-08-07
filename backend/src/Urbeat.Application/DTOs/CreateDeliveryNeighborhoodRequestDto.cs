namespace Urbeat.Application.DTOs;

public sealed class CreateDeliveryNeighborhoodRequestDto
{
    public string Neighborhood { get; init; } = string.Empty;

    public string City { get; init; } = string.Empty;
}
