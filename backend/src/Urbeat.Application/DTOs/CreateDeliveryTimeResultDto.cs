namespace Urbeat.Application.DTOs;

public sealed class CreateDeliveryTimeResultDto
{
    public bool NotFound { get; init; }

    public bool Forbidden { get; init; }

    public bool Conflict { get; init; }

    public DeliveryTimeResponseDto? DeliveryTime { get; init; }
}
