namespace Urbeat.Application.Outbox;

public static class OutboxEventTypes
{
    public const string OrderCreated = "OrderCreated";
    public const string OrderStatusChanged = "OrderStatusChanged";
    public const string OutboundMessageRequested = "OutboundMessageRequested";
}
