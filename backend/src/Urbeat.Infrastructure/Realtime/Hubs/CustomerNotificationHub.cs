using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Urbeat.Infrastructure.Realtime.Hubs;

/// <summary>
/// SignalR Hub for real-time customer notifications (e.g., order status updates, delivery area changes).
/// Clients connect here to receive push notifications about their orders and store coverage.
///
/// Anonymous connections are allowed so the storefront can join a store group (delivery area
/// updates) before login; user-targeted pushes require the JWT supplied by the client.
/// </summary>
[AllowAnonymous]
public class CustomerNotificationHub : Hub
{
    /// <summary>Entra no grupo de notificações da loja (uso anônimo).</summary>
    public async Task JoinStore(string storeId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"store-{storeId}");
    }

    /// <summary>Sai do grupo de notificações da loja (uso anônimo).</summary>
    public async Task LeaveStore(string storeId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"store-{storeId}");
    }
}
