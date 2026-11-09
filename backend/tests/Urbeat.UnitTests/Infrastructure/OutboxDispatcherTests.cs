using FluentAssertions;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Outbox;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Outbox;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Urbeat.UnitTests.Infrastructure;

public sealed class OutboxDispatcherTests : IDisposable
{
    private const string BusinessType = "OrderCreated";
    private const string CommunicationsType = "OutboundMessageRequested";

    private readonly ApplicationDbContext _db;
    private readonly string _dbName;

    public OutboxDispatcherTests()
    {
        _dbName = $"urbeat-outbox-dispatcher-{Guid.NewGuid()}";
        _db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(_dbName).Options);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private OutboxDispatcher CreateDispatcher(params FakeHandler[] handlers)
    {
        var options = Options.Create(new OutboxOptions
        {
            BatchSize = 10,
            MaxAttempts = 3,
            LockDuration = TimeSpan.FromSeconds(30),
            RetryBaseDelay = TimeSpan.FromSeconds(1)
        });
        return new OutboxDispatcher(
            _db,
            handlers,
            new FakeClassifier(),
            options,
            NullLogger<OutboxDispatcher>.Instance);
    }

    private async Task<OutboxMessage> SeedMessageAsync(
        string type,
        string payload = "{}",
        OutboxMessageStatus status = OutboxMessageStatus.Pending,
        DateTime? availableAtUtc = null,
        DateTime? lockedUntilUtc = null,
        Guid? aggregateId = null,
        long sequence = 0)
    {
        var message = new OutboxMessage
        {
            Type = type,
            AggregateId = aggregateId ?? Guid.NewGuid(),
            Sequence = sequence,
            Payload = payload,
            OccurredAtUtc = DateTime.UtcNow,
            AvailableAtUtc = availableAtUtc ?? DateTime.UtcNow,
            Status = status,
            LockedUntilUtc = lockedUntilUtc
        };
        _db.OutboxMessages.Add(message);
        await _db.SaveChangesAsync();
        return message;
    }

    private async Task<OutboxHandlerDelivery> SeedDeliveryAsync(
        OutboxMessage message,
        string handlerKey,
        OutboxChannel channel,
        OutboxHandlerDeliveryStatus status,
        DateTime? availableAtUtc = null,
        DateTime? lockedUntilUtc = null,
        int attemptCount = 0,
        string? lastError = null)
    {
        var delivery = new OutboxHandlerDelivery
        {
            OutboxMessageId = message.Id,
            HandlerKey = handlerKey,
            Channel = channel,
            AggregateId = message.AggregateId,
            Sequence = message.Sequence,
            Status = status,
            AttemptCount = attemptCount,
            AvailableAtUtc = availableAtUtc ?? DateTime.UtcNow,
            LockedUntilUtc = lockedUntilUtc,
            LastError = lastError
        };
        _db.OutboxHandlerDeliveries.Add(delivery);
        await _db.SaveChangesAsync();
        return delivery;
    }

    [Fact]
    public async Task DispatchBatchAsync_ShouldFanOutAndProcessOnlyTheRequestedChannel()
    {
        var business = new FakeHandler(OutboxHandlerKeys.Notification, OutboxChannel.Business, BusinessType);
        var communications = new FakeHandler(OutboxHandlerKeys.Email, OutboxChannel.Communications, BusinessType);
        var message = await SeedMessageAsync(BusinessType);

        var dispatcher = CreateDispatcher(business, communications);

        var processed = await dispatcher.DispatchBatchAsync(OutboxChannel.Business);

        processed.Should().Be(1);
        business.Invocations.Should().Be(1);
        communications.Invocations.Should().Be(0);

        var deliveries = await _db.OutboxHandlerDeliveries
            .Where(x => x.OutboxMessageId == message.Id)
            .OrderBy(x => x.Channel)
            .ToListAsync();

        deliveries.Should().HaveCount(2);
        deliveries.Single(x => x.Channel == OutboxChannel.Business).Status
            .Should().Be(OutboxHandlerDeliveryStatus.Processed);
        deliveries.Single(x => x.Channel == OutboxChannel.Communications).Status
            .Should().Be(OutboxHandlerDeliveryStatus.Pending);

        (await _db.OutboxMessages.SingleAsync()).Status.Should().Be(OutboxMessageStatus.Pending);

        await dispatcher.DispatchBatchAsync(OutboxChannel.Communications);

        communications.Invocations.Should().Be(1);
        (await _db.OutboxMessages.SingleAsync()).Status.Should().Be(OutboxMessageStatus.Processed);
    }

    [Fact]
    public async Task DispatchBatchAsync_ShouldBeIdempotent_WhenFanOutRunsRepeatedly()
    {
        var handler = new FakeHandler(OutboxHandlerKeys.Notification, OutboxChannel.Business, BusinessType);
        var communications = new FakeHandler(OutboxHandlerKeys.Email, OutboxChannel.Communications, BusinessType);
        var message = await SeedMessageAsync(BusinessType);

        var dispatcher = CreateDispatcher(handler, communications);
        await dispatcher.DispatchBatchAsync(OutboxChannel.Business);
        await dispatcher.DispatchBatchAsync(OutboxChannel.Business);

        handler.Invocations.Should().Be(1);
        (await _db.OutboxHandlerDeliveries.CountAsync(x => x.OutboxMessageId == message.Id))
            .Should().Be(2);
    }

    [Fact]
    public async Task DispatchBatchAsync_ShouldIsolateFailure_FromSiblingHandlers()
    {
        var ok = new FakeHandler(OutboxHandlerKeys.Notification, OutboxChannel.Business, BusinessType);
        var fail = new FakeHandler(
            OutboxHandlerKeys.Print,
            OutboxChannel.Business,
            BusinessType,
            _ => Task.FromResult(OutboxProcessingResult.Fail("printer offline")));
        var communications = new FakeHandler(OutboxHandlerKeys.Email, OutboxChannel.Communications, BusinessType);
        var message = await SeedMessageAsync(BusinessType);

        var dispatcher = CreateDispatcher(ok, fail, communications);
        await dispatcher.DispatchBatchAsync(OutboxChannel.Business);

        var deliveries = await _db.OutboxHandlerDeliveries
            .Where(x => x.OutboxMessageId == message.Id)
            .ToListAsync();

        deliveries.Single(x => x.HandlerKey == OutboxHandlerKeys.Notification).Status
            .Should().Be(OutboxHandlerDeliveryStatus.Processed);
        deliveries.Single(x => x.HandlerKey == OutboxHandlerKeys.Print).Status
            .Should().Be(OutboxHandlerDeliveryStatus.Failed);
        deliveries.Single(x => x.HandlerKey == OutboxHandlerKeys.Email).Status
            .Should().Be(OutboxHandlerDeliveryStatus.Pending);

        (await _db.OutboxMessages.SingleAsync()).Status.Should().Be(OutboxMessageStatus.Pending);

        // The failed business delivery never blocks the communications lane.
        await dispatcher.DispatchBatchAsync(OutboxChannel.Communications);
        communications.Invocations.Should().Be(1);

        deliveries = await _db.OutboxHandlerDeliveries
            .Where(x => x.OutboxMessageId == message.Id)
            .ToListAsync();
        deliveries.Single(x => x.HandlerKey == OutboxHandlerKeys.Email).Status
            .Should().Be(OutboxHandlerDeliveryStatus.Processed);

        // One terminal handler failure keeps the legacy message status Failed without re-running
        // the already-successful siblings.
        (await _db.OutboxMessages.SingleAsync()).Status.Should().Be(OutboxMessageStatus.Failed);
        ok.Invocations.Should().Be(1);
    }

    [Fact]
    public async Task DispatchBatchAsync_ShouldNotMarkBusinessFailure_WhenOnlyEmailFails()
    {
        var business = new FakeHandler(OutboxHandlerKeys.Notification, OutboxChannel.Business, BusinessType);
        var email = new FakeHandler(
            OutboxHandlerKeys.Email,
            OutboxChannel.Communications,
            BusinessType,
            _ => Task.FromResult(OutboxProcessingResult.Fail("invalid recipient")));
        var message = await SeedMessageAsync(BusinessType);

        var dispatcher = CreateDispatcher(business, email);
        await dispatcher.DispatchBatchAsync(OutboxChannel.Business);
        await dispatcher.DispatchBatchAsync(OutboxChannel.Communications);

        var deliveries = await _db.OutboxHandlerDeliveries
            .Where(x => x.OutboxMessageId == message.Id)
            .ToListAsync();

        deliveries.Single(x => x.HandlerKey == OutboxHandlerKeys.Notification).Status
            .Should().Be(OutboxHandlerDeliveryStatus.Processed);
        deliveries.Single(x => x.HandlerKey == OutboxHandlerKeys.Email).Status
            .Should().Be(OutboxHandlerDeliveryStatus.Failed);
        business.Invocations.Should().Be(1);
    }

    [Fact]
    public async Task DispatchBatchAsync_ShouldNotClaimLaterSequence_WhileEarlierIsPending()
    {
        var handler = new FakeHandler(OutboxHandlerKeys.Notification, OutboxChannel.Business, BusinessType);
        var aggregateId = Guid.NewGuid();
        await SeedMessageAsync(BusinessType, aggregateId: aggregateId, sequence: 2);
        await SeedMessageAsync(BusinessType, aggregateId: aggregateId, sequence: 1);

        var dispatcher = CreateDispatcher(handler);

        var processed = await dispatcher.DispatchBatchAsync(OutboxChannel.Business);

        processed.Should().Be(1);
        handler.Invocations.Should().Be(1);

        var deliveries = await _db.OutboxHandlerDeliveries
            .OrderBy(x => x.Sequence)
            .ToListAsync();
        deliveries.Single(x => x.Sequence == 1).Status.Should().Be(OutboxHandlerDeliveryStatus.Processed);
        deliveries.Single(x => x.Sequence == 2).Status.Should().Be(OutboxHandlerDeliveryStatus.Pending);

        await dispatcher.DispatchBatchAsync(OutboxChannel.Business);

        deliveries = await _db.OutboxHandlerDeliveries.OrderBy(x => x.Sequence).ToListAsync();
        deliveries.Should().OnlyContain(x => x.Status == OutboxHandlerDeliveryStatus.Processed);
        handler.Invocations.Should().Be(2);
    }

    [Fact]
    public async Task DispatchBatchAsync_ShouldNotClaimLaterSequence_WhileEarlierIsRetrying()
    {
        var handler = new FakeHandler(OutboxHandlerKeys.Notification, OutboxChannel.Business, BusinessType, message =>
        {
            if (message.Sequence == 1)
            {
                return Task.FromResult(OutboxProcessingResult.Retry("transient"));
            }

            return Task.FromResult(OutboxProcessingResult.Ok());
        });

        var aggregateId = Guid.NewGuid();
        await SeedMessageAsync(BusinessType, aggregateId: aggregateId, sequence: 1);
        await SeedMessageAsync(BusinessType, aggregateId: aggregateId, sequence: 2);

        var dispatcher = CreateDispatcher(handler);
        await dispatcher.DispatchBatchAsync(OutboxChannel.Business);

        var second = await dispatcher.DispatchBatchAsync(OutboxChannel.Business);

        // Sequence 1 is pending retry; sequence 2 must remain unclaimed.
        second.Should().Be(0);
        var deliveries = await _db.OutboxHandlerDeliveries.OrderBy(x => x.Sequence).ToListAsync();
        deliveries.Single(x => x.Sequence == 1).Status.Should().Be(OutboxHandlerDeliveryStatus.Pending);
        deliveries.Single(x => x.Sequence == 2).Status.Should().Be(OutboxHandlerDeliveryStatus.Pending);
    }

    [Fact]
    public async Task DispatchBatchAsync_ShouldTreatSequenceZeroAsUnordered()
    {
        var handler = new FakeHandler(OutboxHandlerKeys.Notification, OutboxChannel.Business, BusinessType);
        var aggregateId = Guid.NewGuid();
        await SeedMessageAsync(BusinessType, aggregateId: aggregateId, sequence: 0);
        await SeedMessageAsync(BusinessType, aggregateId: aggregateId, sequence: 0);

        var dispatcher = CreateDispatcher(handler);
        var processed = await dispatcher.DispatchBatchAsync(OutboxChannel.Business);

        processed.Should().Be(2);
        handler.Invocations.Should().Be(2);
    }

    [Fact]
    public async Task DispatchBatchAsync_ShouldOrderIndependentlyPerChannel()
    {
        var business = new FakeHandler(OutboxHandlerKeys.Notification, OutboxChannel.Business, BusinessType);
        var email = new FakeHandler(OutboxHandlerKeys.Email, OutboxChannel.Communications, BusinessType);

        var aggregateId = Guid.NewGuid();
        await SeedMessageAsync(BusinessType, aggregateId: aggregateId, sequence: 2);
        await SeedMessageAsync(BusinessType, aggregateId: aggregateId, sequence: 1);

        var dispatcher = CreateDispatcher(business, email);
        await dispatcher.DispatchBatchAsync(OutboxChannel.Business);

        // Business sequence 1 is processed, sequence 2 is still pending. Communications must not be
        // blocked by the business backlog and drains both sequences on its own order.
        var firstCommunicationsPass = await dispatcher.DispatchBatchAsync(OutboxChannel.Communications);
        firstCommunicationsPass.Should().Be(1);

        var secondCommunicationsPass = await dispatcher.DispatchBatchAsync(OutboxChannel.Communications);
        secondCommunicationsPass.Should().Be(1);

        email.Invocations.Should().Be(2);
    }

    [Fact]
    public async Task DispatchBatchAsync_ShouldTerminalize_WhenNoHandlerSupportsType()
    {
        var handler = new FakeHandler(OutboxHandlerKeys.Notification, OutboxChannel.Business, "UnrelatedType");
        var message = await SeedMessageAsync(BusinessType);

        var dispatcher = CreateDispatcher(handler);
        var processed = await dispatcher.DispatchBatchAsync(OutboxChannel.Business);

        processed.Should().Be(0);

        var reloaded = await _db.OutboxMessages.SingleAsync();
        reloaded.Status.Should().Be(OutboxMessageStatus.Failed);
        reloaded.LastError.Should().Contain("No handler registered");
        (await _db.OutboxHandlerDeliveries.CountAsync(x => x.OutboxMessageId == message.Id))
            .Should().Be(0);
    }

    [Fact]
    public async Task DispatchBatchAsync_ShouldSanitizeLastError_ByHandlerType()
    {
        var handler = new FakeHandler(
            OutboxHandlerKeys.Email,
            OutboxChannel.Communications,
            CommunicationsType,
            _ => Task.FromResult(OutboxProcessingResult.Fail(
                "failed for user@urbeat.local https://app.urbeat.test/c/TOKEN123 token=secret123")));
        var message = await SeedMessageAsync(CommunicationsType);

        var dispatcher = CreateDispatcher(handler);
        await dispatcher.DispatchBatchAsync(OutboxChannel.Communications);

        var delivery = await _db.OutboxHandlerDeliveries.SingleAsync(x => x.OutboxMessageId == message.Id);
        delivery.Status.Should().Be(OutboxHandlerDeliveryStatus.Failed);
        delivery.LastError.Should().Contain("[email]");
        delivery.LastError.Should().Contain("[url]");
        delivery.LastError.Should().Contain("[redacted]");
        delivery.LastError.Should().NotContain("user@urbeat.local");
        delivery.LastError.Should().NotContain("TOKEN123");
        delivery.LastError.Should().NotContain("secret123");
    }

    [Fact]
    public async Task DispatchBatchAsync_ShouldRetryTransientFailure_WithBackoff()
    {
        var handler = new FakeHandler(
            OutboxHandlerKeys.Email,
            OutboxChannel.Communications,
            CommunicationsType,
            _ => Task.FromResult(OutboxProcessingResult.Retry("transient")));
        var message = await SeedMessageAsync(CommunicationsType);

        var dispatcher = CreateDispatcher(handler);
        await dispatcher.DispatchBatchAsync(OutboxChannel.Communications);

        var delivery = await _db.OutboxHandlerDeliveries.SingleAsync(x => x.OutboxMessageId == message.Id);
        delivery.Status.Should().Be(OutboxHandlerDeliveryStatus.Pending);
        delivery.AttemptCount.Should().Be(1);
        delivery.AvailableAtUtc.Should().BeAfter(DateTime.UtcNow);
        delivery.LockedUntilUtc.Should().BeNull();
        handler.Invocations.Should().Be(1);

        // The immediate next poll must not bypass the backoff.
        (await dispatcher.DispatchBatchAsync(OutboxChannel.Communications)).Should().Be(0);
        handler.Invocations.Should().Be(1);
    }

    [Fact]
    public async Task DispatchBatchAsync_ShouldMarkFailed_AfterMaxAttemptsExceeded()
    {
        var handler = new FakeHandler(
            OutboxHandlerKeys.Email,
            OutboxChannel.Communications,
            CommunicationsType,
            _ => Task.FromResult(OutboxProcessingResult.Retry("still down")));
        var message = await SeedMessageAsync(CommunicationsType);

        var dispatcher = CreateDispatcher(handler);
        for (var i = 0; i < 3; i++)
        {
            await dispatcher.DispatchBatchAsync(OutboxChannel.Communications);
            var current = await _db.OutboxHandlerDeliveries.SingleAsync(x => x.OutboxMessageId == message.Id);
            if (current.Status == OutboxHandlerDeliveryStatus.Failed)
            {
                break;
            }

            current.AvailableAtUtc = DateTime.UtcNow.AddDays(-1);
            await _db.SaveChangesAsync();
        }

        var delivery = await _db.OutboxHandlerDeliveries.SingleAsync(x => x.OutboxMessageId == message.Id);
        delivery.Status.Should().Be(OutboxHandlerDeliveryStatus.Failed);
        delivery.AttemptCount.Should().Be(3);
        (await _db.OutboxMessages.SingleAsync()).Status.Should().Be(OutboxMessageStatus.Failed);
    }

    [Fact]
    public async Task DispatchBatchAsync_ShouldReclaimExpiredProcessingDelivery()
    {
        var handler = new FakeHandler(OutboxHandlerKeys.Notification, OutboxChannel.Business, BusinessType);
        var message = await SeedMessageAsync(BusinessType);
        await SeedDeliveryAsync(
            message,
            OutboxHandlerKeys.Notification,
            OutboxChannel.Business,
            OutboxHandlerDeliveryStatus.Processing,
            lockedUntilUtc: DateTime.UtcNow.AddMinutes(-1));

        var dispatcher = CreateDispatcher(handler);
        var processed = await dispatcher.DispatchBatchAsync(OutboxChannel.Business);

        processed.Should().Be(1);
        handler.Invocations.Should().Be(1);

        var delivery = await _db.OutboxHandlerDeliveries.SingleAsync(x => x.OutboxMessageId == message.Id);
        delivery.Status.Should().Be(OutboxHandlerDeliveryStatus.Processed);
    }

    [Fact]
    public async Task DispatchBatchAsync_ShouldNotClaimLockedDelivery()
    {
        var handler = new FakeHandler(OutboxHandlerKeys.Notification, OutboxChannel.Business, BusinessType);
        var message = await SeedMessageAsync(BusinessType);
        await SeedDeliveryAsync(
            message,
            OutboxHandlerKeys.Notification,
            OutboxChannel.Business,
            OutboxHandlerDeliveryStatus.Processing,
            lockedUntilUtc: DateTime.UtcNow.AddMinutes(10));

        var dispatcher = CreateDispatcher(handler);
        var processed = await dispatcher.DispatchBatchAsync(OutboxChannel.Business);

        processed.Should().Be(0);
        handler.Invocations.Should().Be(0);
    }

    [Fact]
    public async Task DispatchBatchAsync_ShouldNotFanOutOrReplayLegacyProcessedMessage()
    {
        var handler = new FakeHandler(OutboxHandlerKeys.Notification, OutboxChannel.Business, BusinessType);
        var message = await SeedMessageAsync(BusinessType, status: OutboxMessageStatus.Processed);

        var dispatcher = CreateDispatcher(handler);
        var processed = await dispatcher.DispatchBatchAsync(OutboxChannel.Business);

        processed.Should().Be(0);
        handler.Invocations.Should().Be(0);
        (await _db.OutboxHandlerDeliveries.CountAsync(x => x.OutboxMessageId == message.Id))
            .Should().Be(0);
    }

    [Fact]
    public async Task DispatchBatchAsync_ShouldNotFanOutLegacyFailedMessage()
    {
        var handler = new FakeHandler(OutboxHandlerKeys.Notification, OutboxChannel.Business, BusinessType);
        var message = await SeedMessageAsync(BusinessType, status: OutboxMessageStatus.Failed);

        var dispatcher = CreateDispatcher(handler);
        var processed = await dispatcher.DispatchBatchAsync(OutboxChannel.Business);

        processed.Should().Be(0);
        handler.Invocations.Should().Be(0);
        (await _db.OutboxHandlerDeliveries.CountAsync(x => x.OutboxMessageId == message.Id))
            .Should().Be(0);
    }

    [Fact]
    public async Task DispatchBatchAsync_ShouldBackfillPendingLegacyMessage()
    {
        var handler = new FakeHandler(OutboxHandlerKeys.Notification, OutboxChannel.Business, BusinessType);
        var message = await SeedMessageAsync(BusinessType);

        var dispatcher = CreateDispatcher(handler);
        var processed = await dispatcher.DispatchBatchAsync(OutboxChannel.Business);

        processed.Should().Be(1);
        handler.Invocations.Should().Be(1);

        var delivery = await _db.OutboxHandlerDeliveries.SingleAsync(x => x.OutboxMessageId == message.Id);
        delivery.Status.Should().Be(OutboxHandlerDeliveryStatus.Processed);
    }

    [Fact]
    public async Task DispatchBatchAsync_ShouldDeferBackfill_WhileLegacyProcessingLockIsActive()
    {
        var handler = new FakeHandler(OutboxHandlerKeys.Notification, OutboxChannel.Business, BusinessType);
        var message = await SeedMessageAsync(
            BusinessType,
            status: OutboxMessageStatus.Processing,
            lockedUntilUtc: DateTime.UtcNow.AddMinutes(10));

        var dispatcher = CreateDispatcher(handler);
        var processed = await dispatcher.DispatchBatchAsync(OutboxChannel.Business);

        processed.Should().Be(0);
        handler.Invocations.Should().Be(0);
        (await _db.OutboxHandlerDeliveries.CountAsync(x => x.OutboxMessageId == message.Id))
            .Should().Be(0);

        message.LockedUntilUtc = DateTime.UtcNow.AddMinutes(-1);
        await _db.SaveChangesAsync();

        processed = await dispatcher.DispatchBatchAsync(OutboxChannel.Business);
        processed.Should().Be(1);
        handler.Invocations.Should().Be(1);
    }

    [Fact]
    public async Task DispatchBatchAsync_ShouldCreateOneDeliveryPerHandler_WhenHandlerSupportsMultipleTypes()
    {
        var handler = new FakeHandler(
            new[] { BusinessType, "OrderStatusChanged" },
            OutboxHandlerKeys.Notification,
            OutboxChannel.Business);
        await SeedMessageAsync(BusinessType);
        await SeedMessageAsync("OrderStatusChanged");

        var dispatcher = CreateDispatcher(handler);
        await dispatcher.DispatchBatchAsync(OutboxChannel.Business);

        (await _db.OutboxHandlerDeliveries.CountAsync()).Should().Be(2);
        handler.Invocations.Should().Be(2);
    }

    public sealed class FakeClassifier : IOutboxHandlerClassifier
    {
        public OutboxHandlerDescriptor Classify(IOutboxEventHandler handler)
        {
            return handler is FakeHandler fake
                ? new OutboxHandlerDescriptor(fake.HandlerKey, fake.Channel)
                : throw new InvalidOperationException($"Unclassified handler {handler.GetType().Name}.");
        }
    }

    public sealed class FakeHandler : IOutboxEventHandler
    {
        private readonly Func<OutboxMessage, Task<OutboxProcessingResult>> _onHandle;

        public FakeHandler(
            string handlerKey,
            OutboxChannel channel,
            string supportedType,
            Func<OutboxMessage, Task<OutboxProcessingResult>>? onHandle = null)
            : this(new[] { supportedType }, handlerKey, channel, onHandle)
        {
        }

        public FakeHandler(
            IReadOnlyCollection<string> supportedTypes,
            string handlerKey,
            OutboxChannel channel,
            Func<OutboxMessage, Task<OutboxProcessingResult>>? onHandle = null)
        {
            SupportedTypes = supportedTypes;
            HandlerKey = handlerKey;
            Channel = channel;
            _onHandle = onHandle ?? (_ => Task.FromResult(OutboxProcessingResult.Ok()));
        }

        public IReadOnlyCollection<string> SupportedTypes { get; }

        public string HandlerKey { get; }

        public OutboxChannel Channel { get; }

        public int Invocations { get; private set; }

        public Task<OutboxProcessingResult> HandleAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            Invocations++;
            return _onHandle(message);
        }
    }
}
