using Urbeat.Application.Outbox;
using Urbeat.Domain.Entities;

namespace Urbeat.Application.Interfaces;

public interface IOutboxEventHandler
{
    IReadOnlyCollection<string> SupportedTypes { get; }

    Task<OutboxProcessingResult> HandleAsync(OutboxMessage message, CancellationToken cancellationToken = default);
}
