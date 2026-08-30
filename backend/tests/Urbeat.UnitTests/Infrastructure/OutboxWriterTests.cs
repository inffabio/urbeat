using System.Text.Json;
using FluentAssertions;
using Urbeat.Application.Outbox;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Outbox;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.UnitTests.Infrastructure;

public sealed class OutboxWriterTests : IDisposable
{
    private readonly ApplicationDbContext _db;

    public OutboxWriterTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-outbox-writer-{Guid.NewGuid()}")
            .Options;
        _db = new ApplicationDbContext(options);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task EnqueueAsync_ShouldAddPendingMessage_WithoutSaving()
    {
        var writer = new OutboxWriter(_db);
        var aggregateId = Guid.NewGuid();
        var occurredAtUtc = new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc);

        await writer.EnqueueAsync(
            OutboxEventTypes.OrderCreated,
            aggregateId,
            new OrderCreatedEvent
            {
                OrderId = aggregateId,
                StoreId = Guid.NewGuid(),
                CustomerUserId = Guid.NewGuid(),
                SellerUserId = Guid.NewGuid(),
                Code = "URB-ABC12345",
                Status = OrderStatus.Received,
                OccurredAtUtc = occurredAtUtc
            },
            occurredAtUtc,
            aggregateType: "Order");

        var message = _db.OutboxMessages.Local.Single();
        message.Status.Should().Be(OutboxMessageStatus.Pending);
        message.Type.Should().Be(OutboxEventTypes.OrderCreated);
        message.AggregateId.Should().Be(aggregateId);
        message.AggregateType.Should().Be("Order");
        message.OccurredAtUtc.Should().Be(occurredAtUtc);
        message.AvailableAtUtc.Should().Be(occurredAtUtc);
        message.AttemptCount.Should().Be(0);

        // The message is tracked but not persisted until SaveChanges is called by the caller.
        var fresh = await _db.OutboxMessages.AsNoTracking().CountAsync();
        fresh.Should().Be(0);
    }

    [Fact]
    public async Task EnqueueAsync_ShouldSerializeDeterministically_AndNotEmbedSecrets()
    {
        var writer = new OutboxWriter(_db);
        var aggregateId = Guid.NewGuid();
        var occurredAtUtc = new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc);

        await writer.EnqueueAsync(
            OutboxEventTypes.OrderStatusChanged,
            aggregateId,
            new OrderStatusChangedEvent
            {
                OrderId = aggregateId,
                StoreId = Guid.NewGuid(),
                CustomerUserId = Guid.NewGuid(),
                SellerUserId = Guid.NewGuid(),
                Code = "URB-ABC12345",
                PreviousStatus = OrderStatus.Received,
                NewStatus = OrderStatus.Preparing,
                ChangedAtUtc = occurredAtUtc,
                Source = "Seller"
            },
            occurredAtUtc);

        var message = _db.OutboxMessages.Local.Single();
        var json = message.Payload;

        json.Should().Contain("\"newStatus\":\"Preparing\"");
        json.Should().Contain("\"previousStatus\":\"Received\"");
        json.Should().Contain("\"code\":\"URB-ABC12345\"");

        var parsed = JsonSerializer.Deserialize<OrderStatusChangedEvent>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        });
        parsed!.NewStatus.Should().Be(OrderStatus.Preparing);
        parsed.Code.Should().Be("URB-ABC12345");

        // No secrets: the payload only carries the typed contract fields, not tokens or credentials.
        json.Should().NotContain("secret", "payloads must never carry secrets");
        json.Should().NotContain("password", "payloads must never carry credentials");
    }

    [Fact]
    public async Task EnqueueAsync_ShouldRejectNonUtcOccurredAt()
    {
        var writer = new OutboxWriter(_db);

        var act = () => writer.EnqueueAsync(
            OutboxEventTypes.OrderCreated,
            Guid.NewGuid(),
            new OrderCreatedEvent(),
            DateTime.Now);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task EnqueueAsync_ShouldRejectEmptyTypeOrAggregate()
    {
        var writer = new OutboxWriter(_db);
        var utcNow = DateTime.UtcNow;

        var emptyType = () => writer.EnqueueAsync(" ", Guid.NewGuid(), new OrderCreatedEvent(), utcNow);
        await emptyType.Should().ThrowAsync<ArgumentException>();

        var emptyAggregate = () => writer.EnqueueAsync(OutboxEventTypes.OrderCreated, Guid.Empty, new OrderCreatedEvent(), utcNow);
        await emptyAggregate.Should().ThrowAsync<ArgumentException>();
    }
}
