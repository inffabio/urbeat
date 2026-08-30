using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.Infrastructure.Outbox;

public sealed class OutboxDeliveryTracker : IOutboxDeliveryTracker
{
    private readonly ApplicationDbContext _dbContext;

    public OutboxDeliveryTracker(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<bool> IsDeliveredAsync(string deliveryKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deliveryKey);

        return _dbContext.OutboxDeliveries
            .AsNoTracking()
            .AnyAsync(x => x.DeliveryKey == deliveryKey, cancellationToken);
    }

    public async Task RecordDeliveredAsync(
        string deliveryKey,
        string handlerType,
        Guid outboxMessageId,
        int attemptCount,
        string? providerMessageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deliveryKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(handlerType);

        var alreadyRecorded = await _dbContext.OutboxDeliveries
            .AsNoTracking()
            .AnyAsync(x => x.DeliveryKey == deliveryKey, cancellationToken);

        if (alreadyRecorded)
        {
            return;
        }

        _dbContext.OutboxDeliveries.Add(new OutboxDelivery
        {
            OutboxMessageId = outboxMessageId,
            DeliveryKey = deliveryKey,
            HandlerType = handlerType,
            AttemptCount = attemptCount,
            ProviderMessageId = providerMessageId,
            DeliveredAtUtc = DateTime.UtcNow
        });
    }
}
