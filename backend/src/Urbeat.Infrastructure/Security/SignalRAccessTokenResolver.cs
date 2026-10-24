using Microsoft.AspNetCore.Http;

namespace Urbeat.Infrastructure.Security;

/// <summary>
/// Resolves the bearer token sent as a query string parameter by browser SignalR clients.
///
/// Browsers cannot set an <c>Authorization</c> header on the WebSocket handshake, so the ASP.NET
/// Core SignalR client sends the token as <c>?access_token=...</c> instead. The JWT bearer handler
/// only reads the header by default; accepting the query string is therefore required for the hub
/// connection to be authenticated and for <c>Clients.User(userId)</c> to match it.
///
/// The fallback is intentionally limited to the notification hub path so a token in the query
/// string is never accepted by the regular HTTP API.
/// </summary>
public static class SignalRAccessTokenResolver
{
    public const string QueryStringKey = "access_token";

    public const string HubPathPrefix = "/hubs";

    /// <summary>
    /// Returns the token supplied on the query string for a hub request, or <c>null</c> when the
    /// request is not a hub request or carries no token.
    /// </summary>
    public static string? Resolve(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.Path.StartsWithSegments(HubPathPrefix))
        {
            return null;
        }

        var token = request.Query[QueryStringKey].ToString();
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }
}
