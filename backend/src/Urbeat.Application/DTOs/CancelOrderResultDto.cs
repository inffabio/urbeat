namespace Urbeat.Application.DTOs;

public sealed class CancelOrderResultDto
{
    public bool NotFound { get; init; }

    public bool Forbidden { get; init; }

    /// <summary>The order status does not allow a cancellation by the store (only Received/Preparing).</summary>
    public bool InvalidState { get; init; }

    /// <summary>The reason is missing or shorter than the minimum accepted by the server.</summary>
    public bool ReasonRequired { get; init; }

    /// <summary>The trimmed reason is longer than the persisted column accepts.</summary>
    public bool ReasonTooLong { get; init; }

    public bool ConcurrentUpdate { get; init; }

    public OrderDetailsResponseDto? Order { get; init; }
}
