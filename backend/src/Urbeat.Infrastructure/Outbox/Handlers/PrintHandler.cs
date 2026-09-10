using Urbeat.Application.Interfaces;
using Urbeat.Application.Outbox;
using Urbeat.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Urbeat.Infrastructure.Outbox.Handlers;

/// <summary>
/// Best-effort print delivery for a newly created order.
///
/// The physical printer is a local (loopback) agent reached from the seller's browser/Capacitor
/// app (see <c>print-agent/</c>); there is currently no backend-to-agent delivery channel. When the
/// print agent is unavailable or unwired, this handler logs a warning and reports success so the
/// durable outbox message is processed (marked complete) instead of becoming Failed and blocking the
/// order/event pipeline.
/// </summary>
public sealed class PrintHandler : IOutboxEventHandler
{
    private readonly ILogger<PrintHandler> _logger;

    public PrintHandler(ILogger<PrintHandler> logger)
    {
        _logger = logger;
    }

    public IReadOnlyCollection<string> SupportedTypes => new[] { OutboxEventTypes.OrderCreated };

    public Task<OutboxProcessingResult> HandleAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        var evt = OutboxPayloadSerializer.Deserialize<OrderCreatedEvent>(message.Payload);
        var jobKey = $"order:{evt.OrderId}";

        _logger.LogWarning(
            "Print delivery skipped as best-effort | OrderId={OrderId} | Code={Code} | JobKey={JobKey} | Reason={Reason}",
            evt.OrderId, evt.Code, jobKey, "No backend-to-print-agent delivery channel is available.");

        return Task.FromResult(OutboxProcessingResult.Ok());
    }
}
