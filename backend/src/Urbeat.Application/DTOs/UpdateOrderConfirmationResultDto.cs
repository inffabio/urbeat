namespace Urbeat.Application.DTOs;

public sealed class UpdateOrderConfirmationResultDto
{
    public bool NotFound { get; init; }

    public bool Forbidden { get; init; }

    public bool InvalidState { get; init; }

    public OrderDetailsResponseDto? Order { get; init; }
}
