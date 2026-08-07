namespace Urbeat.Application.DTOs;

public sealed class UpdateOrderStatusResultDto
{
    public bool NotFound { get; init; }

    public bool Forbidden { get; init; }

    public bool InvalidTransition { get; init; }

    public OrderDetailsResponseDto? Order { get; init; }
}
