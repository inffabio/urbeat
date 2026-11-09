using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;

namespace Urbeat.Application.Outbox;

/// <summary>Stable identity and processing lane of an outbox handler.</summary>
public sealed record OutboxHandlerDescriptor(string HandlerKey, OutboxChannel Channel);

/// <summary>
/// Central classification of handlers into the business or communications lane. The dispatcher
/// uses it to fan a message out into one delivery per handler and to restrict a worker to its lane.
/// </summary>
public interface IOutboxHandlerClassifier
{
    OutboxHandlerDescriptor Classify(IOutboxEventHandler handler);
}
