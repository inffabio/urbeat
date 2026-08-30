using Urbeat.Application.Interfaces;
using Urbeat.Application.Outbox;
using Urbeat.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Urbeat.Infrastructure.Outbox.Handlers;

/// <summary>
/// Print delivery for a newly created order.
///
/// The physical printer is a local (loopback) agent reached from the seller's browser/Capacitor
/// app (see <c>print-agent/</c>); there is currently no backend-to-agent delivery channel. This
/// handler must therefore NOT report success for work it did not perform. Instead it fails
/// explicitly so the outbox retains the job in the terminal queue for operational review rather
/// than silently dropping the print intent.
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
            "Print delivery not wired | OrderId={OrderId} | Code={Code} | JobKey={JobKey} | Reason={Reason}",
            evt.OrderId, evt.Code, jobKey, "No backend-to-print-agent delivery channel is available.");

        return Task.FromResult(OutboxProcessingResult.Fail(
            "Print delivery is not wired: the print agent is a local loopback agent unreachable from the server. Job retained for operational review."));
    }
}
