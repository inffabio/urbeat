namespace Urbeat.Application.Outbox;

/// <summary>
/// Stable handler identities persisted in <c>OutboxHandlerDeliveries</c>. Keys are part of the
/// durable contract: renaming a key without a data backfill strands pending deliveries, so keep them
/// stable even if the handler class is renamed.
/// </summary>
public static class OutboxHandlerKeys
{
    public const string Notification = "business.notification";
    public const string OrderSignalR = "business.order-signalr";
    public const string OrderAcceptedForPrintingSignalR = "business.order-accepted-for-printing-signalr";
    public const string Print = "business.print";

    public const string Email = "communications.email";

    /// <summary>Reserved for a future WhatsApp outbox handler; no event flow uses it yet.</summary>
    public const string WhatsApp = "communications.whatsapp";
}
