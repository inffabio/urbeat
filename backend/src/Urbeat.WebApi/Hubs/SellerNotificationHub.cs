using Microsoft.AspNetCore.SignalR;

namespace Urbeat.WebApi.Hubs;

/// <summary>
/// SignalR Hub for real-time seller/store owner notifications (e.g., new incoming orders).
/// Clients connect here to receive push notifications about their store activity.
/// </summary>
public class SellerNotificationHub : Hub
{
    // The frontend can listen for "NewOrderReceived" or "OrderStatusUpdated" events.
    // To send an update from the backend, inject IHubContext<SellerNotificationHub> 
    // into your OrderService or Controller and call:
    // await _hubContext.Clients.User(sellerUserId).SendAsync("NewOrderReceived", new { orderId = "...", storeId = "..." });
}
