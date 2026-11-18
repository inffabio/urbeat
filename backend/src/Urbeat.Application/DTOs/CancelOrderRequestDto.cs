namespace Urbeat.Application.DTOs;

/// <summary>
/// Seller-initiated cancellation. Only the reason is accepted from the client; the actor
/// (<see cref="Domain.Entities.OrderCancelledBy.Store"/>), status and timestamps are server-derived.
/// </summary>
public sealed class CancelOrderRequestDto
{
    /// <summary>Minimum accepted length for the cancellation reason, enforced by the API and service.</summary>
    public const int MinimumReasonLength = 5;

    /// <summary>Maximum length for the cancellation reason.</summary>
    public const int MaximumReasonLength = 500;

    public string? Reason { get; init; }
}
