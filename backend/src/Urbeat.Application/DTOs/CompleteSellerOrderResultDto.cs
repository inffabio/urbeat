namespace Urbeat.Application.DTOs;

public sealed class CompleteSellerOrderResultDto
{
    public bool NotFound { get; init; }

    public bool Forbidden { get; init; }

    public bool InvalidState { get; init; }

    public OrderDetailsResponseDto? Order { get; init; }
}
