using Urbeat.Application.Interfaces;
using Urbeat.Application.Outbox;
using Urbeat.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Urbeat.Infrastructure.Outbox.Handlers;

/// <summary>
/// Best-effort live SignalR push for seller-driven order status changes.
///
/// SignalR has no server-side idempotency key, so delivery is at-least-once. A crash after the push
/// but before the message is marked processed (or a claim re-acquisition after lock expiry) can
/// deliver the same live event more than once. Clients must tolerate duplicates; the durable
/// notification rows and the existing list/polling endpoint remain the authoritative source of
/// truth. We therefore do NOT record a delivery marker here — doing so would falsely imply
/// exactly-once and could suppress a legitimate retry.
///
/// Because the durable rows/endpoints are authoritative, a live push that cannot be delivered is
/// treated as best-effort: the failure is logged and the handler reports success so the underlying
/// order/event outbox processing completes instead of leaving the message Failed on retries.
/// </summary>
public sealed class OrderSignalRHandler : IOutboxEventHandler
{
    private readonly INotificationService _notificationService;
    private readonly ILogger<OrderSignalRHandler> _logger;

    public OrderSignalRHandler(INotificationService notificationService, ILogger<OrderSignalRHandler> logger)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    public IReadOnlyCollection<string> SupportedTypes => new[] { OutboxEventTypes.OrderStatusChanged };

    public async Task<OutboxProcessingResult> HandleAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        var evt = OutboxPayloadSerializer.Deserialize<OrderStatusChangedEvent>(message.Payload);

        if (!string.Equals(evt.Source, "Seller", StringComparison.OrdinalIgnoreCase))
        {
            return OutboxProcessingResult.Ok();
        }

        var delivered = await _notificationService.NotifyCustomerOrderStatusUpdatedAsync(
            evt.CustomerUserId,
            evt.OrderId,
            evt.Code,
            evt.NewStatus,
            evt.ChangedAtUtc,
            cancellationToken);

        if (!delivered)
        {
            _logger.LogWarning(
                "SignalR live push not delivered; skipping as best-effort | CustomerUserId={CustomerUserId} | OrderId={OrderId} | Code={Code} | NewStatus={NewStatus}",
                evt.CustomerUserId, evt.OrderId, evt.Code, evt.NewStatus);
        }

        return OutboxProcessingResult.Ok();
    }
}
