using System.Text.Json;
using System.Text.Json.Serialization;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;

namespace Urbeat.Infrastructure.Outbox;

public sealed class OutboxWriter : IOutboxWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ApplicationDbContext _dbContext;

    public OutboxWriter(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task EnqueueAsync<T>(
        string type,
        Guid aggregateId,
        T payload,
        DateTime occurredAtUtc,
        long sequence = 0,
        string? aggregateType = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);

        if (occurredAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("OccurredAtUtc must be expressed in UTC.", nameof(occurredAtUtc));
        }

        if (aggregateId == Guid.Empty)
        {
            throw new ArgumentException("AggregateId must not be empty.", nameof(aggregateId));
        }

        var message = new OutboxMessage
        {
            Type = type,
            AggregateId = aggregateId,
            AggregateType = aggregateType,
            Sequence = sequence,
            Payload = payload is null ? "{}" : JsonSerializer.Serialize(payload, SerializerOptions),
            OccurredAtUtc = occurredAtUtc,
            AvailableAtUtc = occurredAtUtc,
            Status = OutboxMessageStatus.Pending
        };

        _dbContext.OutboxMessages.Add(message);

        return Task.CompletedTask;
    }
}
