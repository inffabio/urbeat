using Urbeat.Application.Interfaces;
using Urbeat.Application.Outbox;
using Urbeat.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Urbeat.Infrastructure.Outbox.Handlers;

/// <summary>
/// Delivers the durable acceptance event to the seller hub so the dashboard can enqueue the local
/// automatic print. SignalR is at-least-once; the dashboard deduplicates by the order's acceptance
/// identity and reconciles pending accepted orders from the store orders endpoint after a reload or
/// reconnect.
/// </summary>
public sealed class OrderAcceptedForPrintingSignalRHandler : IOutboxEventHandler
{
    private readonly INotificationService _notificationService;
    private readonly ILogger<OrderAcceptedForPrintingSignalRHandler> _logger;

    public OrderAcceptedForPrintingSignalRHandler(
        INotificationService notificationService,
        ILogger<OrderAcceptedForPrintingSignalRHandler> logger)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    public IReadOnlyCollection<string> SupportedTypes => new[] { OutboxEventTypes.OrderAcceptedForPrinting };

    public async Task<OutboxProcessingResult> HandleAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        var accepted = OutboxPayloadSerializer.Deserialize<OrderAcceptedForPrintingEvent>(message.Payload);

        if (accepted.OrderId == Guid.Empty || accepted.StoreId == Guid.Empty || accepted.SellerUserId == Guid.Empty)
        {
            return OutboxProcessingResult.Fail("Order acceptance event is missing order, store, or seller identifiers.");
        }

        var delivered = await _notificationService.NotifySellerOrderAcceptedForPrintingAsync(
            accepted.SellerUserId,
            accepted.OrderId,
            accepted.StoreId,
            accepted.Code,
            accepted.Sequence,
            accepted.AcceptedAtUtc,
            cancellationToken);

        if (!delivered)
        {
            _logger.LogWarning(
                "Seller acceptance push not delivered; scheduling retry | SellerUserId={SellerUserId} | StoreId={StoreId} | OrderId={OrderId} | Sequence={Sequence}",
                accepted.SellerUserId, accepted.StoreId, accepted.OrderId, accepted.Sequence);

            return OutboxProcessingResult.Retry("Seller acceptance push failed; will retry.");
        }

        return OutboxProcessingResult.Ok();
    }
}
