namespace Urbeat.Application.DTOs;

public sealed class UpdateOrderStatusResultDto
{
    public bool NotFound { get; init; }

    public bool Forbidden { get; init; }

    public bool InvalidTransition { get; init; }

    public bool ConcurrentUpdate { get; init; }

    /// <summary>The transition to OnDelivery requires an active driver from the seller's store.</summary>
    public bool DriverRequired { get; init; }

    /// <summary>The supplied driver is inactive, from another store or otherwise unavailable.</summary>
    public bool DriverNotAvailable { get; init; }

    public OrderDetailsResponseDto? Order { get; init; }
}
