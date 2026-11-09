using Urbeat.Application.Interfaces;
using Urbeat.Application.Outbox;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Outbox.Handlers;

namespace Urbeat.Infrastructure.Outbox;

/// <summary>
/// Single source of truth for the worker/channel of every outbox handler. Adding a handler requires
/// an explicit entry here; the dispatcher refuses to run an unclassified handler instead of silently
/// processing it in the wrong worker.
/// </summary>
public sealed class OutboxHandlerClassifier : IOutboxHandlerClassifier
{
    private static readonly IReadOnlyDictionary<Type, OutboxHandlerDescriptor> Descriptors =
        new Dictionary<Type, OutboxHandlerDescriptor>
        {
            [typeof(NotificationHandler)] = new(OutboxHandlerKeys.Notification, OutboxChannel.Business),
            [typeof(OrderSignalRHandler)] = new(OutboxHandlerKeys.OrderSignalR, OutboxChannel.Business),
            [typeof(OrderAcceptedForPrintingSignalRHandler)] =
                new(OutboxHandlerKeys.OrderAcceptedForPrintingSignalR, OutboxChannel.Business),
            [typeof(PrintHandler)] = new(OutboxHandlerKeys.Print, OutboxChannel.Business),
            [typeof(EmailHandler)] = new(OutboxHandlerKeys.Email, OutboxChannel.Communications)

            // Future WhatsApp delivery: add a handler and map it to
            // new(OutboxHandlerKeys.WhatsApp, OutboxChannel.Communications). No business event is
            // produced for that channel today, so no delivery exists to process. When it is added,
            // prefer a channel-specific event type (or a fan-out predicate) so EmailHandler does not
            // also claim a WhatsApp OutboundMessageRequested and fail on the unsupported channel.
        };

    public OutboxHandlerDescriptor Classify(IOutboxEventHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Classify(handler.GetType());
    }

    /// <summary>
    /// Classifies by type so registration checks and tests do not need to build handlers with all of
    /// their scoped dependencies.
    /// </summary>
    public OutboxHandlerDescriptor Classify(Type handlerType)
    {
        ArgumentNullException.ThrowIfNull(handlerType);

        if (Descriptors.TryGetValue(handlerType, out var descriptor))
        {
            return descriptor;
        }

        throw new InvalidOperationException(
            $"Outbox handler '{handlerType.FullName}' has no channel classification. " +
            $"Add it to {nameof(OutboxHandlerClassifier)} before registering it.");
    }
}
