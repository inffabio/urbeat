using Microsoft.AspNetCore.SignalR;

namespace Urbeat.Infrastructure.Realtime.Hubs;

/// <summary>
/// SignalR Hub for real-time seller/store owner notifications (e.g., new incoming orders).
/// Clients connect here to receive push notifications about their store activity.
///
/// The hub type lives in Infrastructure because both hosts need the typed
/// <c>IHubContext&lt;THub&gt;</c>: the WebApi maps/hosts the hub, while the outbox worker publishes
/// through the shared Redis backplane. The hub route is mapped by the WebApi only.
/// </summary>
public class SellerNotificationHub : Hub
{
    // The frontend listens for "ReceiveSellerNotification" and "OrderStatusUpdated" events.
    // The backend pushes through <see cref="Urbeat.Infrastructure.Realtime.SignalRLiveNotificationPublisher"/>.
}
