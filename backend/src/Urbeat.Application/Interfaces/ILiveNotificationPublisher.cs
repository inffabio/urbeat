namespace Urbeat.Application.Interfaces;

/// <summary>
/// Delivers the durable notifications produced by the outbox to the live channels of a seller or
/// customer. The interface is intentionally free of ASP.NET/SignalR types so the Application and
/// Infrastructure layers never reference the transport host; WebApi owns the concrete SignalR
/// implementation.
///
/// Every method returns <c>true</c> when the push was handed to the transport and <c>false</c> when
/// a real delivery attempt failed. Callers (the outbox handlers) use that signal to schedule a
/// retry; the durable notification rows and the HTTP endpoints remain the source of truth.
/// </summary>
public interface ILiveNotificationPublisher
{
    /// <summary>Pushes a live event to a single seller user.</summary>
    /// <param name="userId">Recipient seller user identifier.</param>
    /// <param name="method">Client event name.</param>
    /// <param name="payload">Event payload. Implementations must never log it.</param>
    /// <param name="cancellationToken">Caller cancellation token.</param>
    Task<bool> PublishSellerAsync(
        Guid userId,
        string method,
        object payload,
        CancellationToken cancellationToken = default);

    /// <summary>Pushes a live event to a single customer user.</summary>
    /// <param name="userId">Recipient customer user identifier.</param>
    /// <param name="method">Client event name.</param>
    /// <param name="payload">Event payload. Implementations must never log it.</param>
    /// <param name="cancellationToken">Caller cancellation token.</param>
    Task<bool> PublishCustomerAsync(
        Guid userId,
        string method,
        object payload,
        CancellationToken cancellationToken = default);
}
