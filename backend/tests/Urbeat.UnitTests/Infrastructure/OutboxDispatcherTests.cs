using FluentAssertions;
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
        return new OutboxDispatcher(_db, handlers, options, NullLogger<OutboxDispatcher>.Instance);
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

    [Fact]
    public async Task DispatchBatchAsync_ShouldRouteToMatchingHandler_AndMarkProcessed()
    {
        var handled = new List<OutboxMessage>();
        var handler = new FakeHandler("OrderCreated", m => { handled.Add(m); return Task.FromResult(OutboxProcessingResult.Ok()); });
        var message = await SeedMessageAsync("OrderCreated");

        var dispatcher = CreateDispatcher(handler);
        var count = await dispatcher.DispatchBatchAsync();

        count.Should().Be(1);
        handled.Should().ContainSingle(x => x.Id == message.Id);

        using var verify = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(_dbName).Options);
        var reloaded = await verify.OutboxMessages.SingleAsync();
        reloaded.Status.Should().Be(OutboxMessageStatus.Processed);
        reloaded.ProcessedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task DispatchBatchAsync_ShouldTerminalize_WhenNoHandlerSupportsType()
    {
        var handler = new FakeHandler("UnrelatedType", _ => Task.FromResult(OutboxProcessingResult.Ok()));
        await SeedMessageAsync("OrderCreated");

        var dispatcher = CreateDispatcher(handler);
        await dispatcher.DispatchBatchAsync();

        var reloaded = await _db.OutboxMessages.SingleAsync();
        reloaded.Status.Should().Be(OutboxMessageStatus.Failed);
        reloaded.LastError.Should().Contain("No handler registered");
    }

    [Fact]
    public async Task DispatchBatchAsync_ShouldNotClaimLaterSequence_WhileEarlierIsPending()
    {
        var handled = new List<OutboxMessage>();
        var handler = new FakeHandler("OrderStatusChanged", m => { handled.Add(m); return Task.FromResult(OutboxProcessingResult.Ok()); });

        var aggregateId = Guid.NewGuid();
        await SeedMessageAsync("OrderStatusChanged", payload: "{}", status: OutboxMessageStatus.Pending, aggregateId: aggregateId, sequence: 2);
        await SeedMessageAsync("OrderStatusChanged", payload: "{}", status: OutboxMessageStatus.Pending, aggregateId: aggregateId, sequence: 1);

        var dispatcher = CreateDispatcher(handler);
        var count = await dispatcher.DispatchBatchAsync();

        // Only the earliest sequence may be claimed while a later one is pending.
        count.Should().Be(1);
        handled.Should().ContainSingle(x => x.Sequence == 1);
    }

    [Fact]
    public async Task DispatchBatchAsync_ShouldClaimLaterSequence_WhenEarlierIsProcessed()
    {
        var handled = new List<OutboxMessage>();
        var handler = new FakeHandler("OrderStatusChanged", m => { handled.Add(m); return Task.FromResult(OutboxProcessingResult.Ok()); });

        var aggregateId = Guid.NewGuid();
        await SeedMessageAsync("OrderStatusChanged", payload: "{}", status: OutboxMessageStatus.Processed, aggregateId: aggregateId, sequence: 1);
        await SeedMessageAsync("OrderStatusChanged", payload: "{}", status: OutboxMessageStatus.Pending, aggregateId: aggregateId, sequence: 2);

        var dispatcher = CreateDispatcher(handler);
        await dispatcher.DispatchBatchAsync();

        handled.Should().ContainSingle(x => x.Sequence == 2);
    }

    [Fact]
    public async Task DispatchBatchAsync_ShouldSanitizeLastError_ByHandlerType()
    {
        var handler = new FakeHandler("OrderCreated", _ => Task.FromResult(OutboxProcessingResult.Fail("failed for user@urbeat.local https://app.urbeat.test/c/TOKEN123 token=secret123")));
        await SeedMessageAsync("OrderCreated");

        var dispatcher = CreateDispatcher(handler);
        await dispatcher.DispatchBatchAsync();

        var reloaded = await _db.OutboxMessages.SingleAsync();
        reloaded.Status.Should().Be(OutboxMessageStatus.Failed);
        reloaded.LastError.Should().Contain("[email]");
        reloaded.LastError.Should().Contain("[url]");
        reloaded.LastError.Should().Contain("[redacted]");
        reloaded.LastError.Should().NotContain("user@urbeat.local");
        reloaded.LastError.Should().NotContain("TOKEN123");
        reloaded.LastError.Should().NotContain("secret123");
    }

    [Fact]
    public async Task DispatchBatchAsync_ShouldRetryTransientFailure_WithBackoff()
    {
        var attempts = 0;
        var handler = new FakeHandler("OrderCreated", _ =>
        {
            attempts++;
            return Task.FromResult(OutboxProcessingResult.Retry("transient"));
        });
        var message = await SeedMessageAsync("OrderCreated");

        var dispatcher = CreateDispatcher(handler);
        await dispatcher.DispatchBatchAsync();

        attempts.Should().Be(1);
        var reloaded = await _db.OutboxMessages.SingleAsync();
        reloaded.Status.Should().Be(OutboxMessageStatus.Pending);
        reloaded.AttemptCount.Should().Be(1);
        reloaded.AvailableAtUtc.Should().BeAfter(DateTime.UtcNow);
        reloaded.LockedUntilUtc.Should().BeNull();
    }

    [Fact]
    public async Task DispatchBatchAsync_ShouldMarkFailed_OnPermanentFailure()
    {
        var handler = new FakeHandler("OrderCreated", _ => Task.FromResult(OutboxProcessingResult.Fail("invalid recipient")));
        var message = await SeedMessageAsync("OrderCreated");

        var dispatcher = CreateDispatcher(handler);
        await dispatcher.DispatchBatchAsync();

        var reloaded = await _db.OutboxMessages.SingleAsync();
        reloaded.Status.Should().Be(OutboxMessageStatus.Failed);
        reloaded.LastError.Should().Contain("invalid recipient");
        reloaded.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task DispatchBatchAsync_ShouldMarkFailed_AfterMaxAttemptsExceeded()
    {
        var handler = new FakeHandler("OrderCreated", _ => Task.FromResult(OutboxProcessingResult.Retry("still down")));
        var message = await SeedMessageAsync("OrderCreated");

        var dispatcher = CreateDispatcher(handler);
        for (var i = 0; i < 3; i++)
        {
            await dispatcher.DispatchBatchAsync();
            var current = await _db.OutboxMessages.SingleAsync();
            if (current.Status == OutboxMessageStatus.Failed)
            {
                break;
            }

            current.AvailableAtUtc = DateTime.UtcNow.AddDays(-1);
            current.Status = OutboxMessageStatus.Pending;
            await _db.SaveChangesAsync();
        }

        var reloaded = await _db.OutboxMessages.SingleAsync();
        reloaded.Status.Should().Be(OutboxMessageStatus.Failed);
        reloaded.AttemptCount.Should().Be(3);
    }

    [Fact]
    public async Task DispatchBatchAsync_ShouldReclaimStaleProcessingMessages()
    {
        var handled = new List<OutboxMessage>();
        var handler = new FakeHandler("OrderCreated", m => { handled.Add(m); return Task.FromResult(OutboxProcessingResult.Ok()); });
        var message = await SeedMessageAsync(
            "OrderCreated",
            status: OutboxMessageStatus.Processing,
            lockedUntilUtc: DateTime.UtcNow.AddMinutes(-1));

        var dispatcher = CreateDispatcher(handler);
        await dispatcher.DispatchBatchAsync();

        handled.Should().ContainSingle(x => x.Id == message.Id);
        var reloaded = await _db.OutboxMessages.SingleAsync();
        reloaded.Status.Should().Be(OutboxMessageStatus.Processed);
    }

    [Fact]
    public async Task DispatchBatchAsync_ShouldNotClaimLockedProcessingMessages()
    {
        var handler = new FakeHandler("OrderCreated", _ => Task.FromResult(OutboxProcessingResult.Ok()));
        await SeedMessageAsync(
            "OrderCreated",
            status: OutboxMessageStatus.Processing,
            lockedUntilUtc: DateTime.UtcNow.AddMinutes(10));

        var dispatcher = CreateDispatcher(handler);
        var count = await dispatcher.DispatchBatchAsync();

        count.Should().Be(0);
    }

    [Fact]
    public async Task DispatchBatchAsync_ShouldInvokeAllMatchingHandlers()
    {
        var first = new FakeHandler("OrderCreated", _ => Task.FromResult(OutboxProcessingResult.Ok()));
        var second = new FakeHandler("OrderCreated", _ => Task.FromResult(OutboxProcessingResult.Ok()));
        var message = await SeedMessageAsync("OrderCreated");

        var dispatcher = CreateDispatcher(first, second);
        await dispatcher.DispatchBatchAsync();

        first.Invocations.Should().Be(1);
        second.Invocations.Should().Be(1);
    }

    [Fact]
    public async Task DispatchBatchAsync_ShouldMarkFailed_WhenAnyHandlerFailsPermanently()
    {
        var ok = new FakeHandler("OrderCreated", _ => Task.FromResult(OutboxProcessingResult.Ok()));
        var fail = new FakeHandler("OrderCreated", _ => Task.FromResult(OutboxProcessingResult.Fail("permanent")));
        await SeedMessageAsync("OrderCreated");

        var dispatcher = CreateDispatcher(ok, fail);
        await dispatcher.DispatchBatchAsync();

        var reloaded = await _db.OutboxMessages.SingleAsync();
        reloaded.Status.Should().Be(OutboxMessageStatus.Failed);
        reloaded.LastError.Should().Contain("permanent");
    }

    public sealed class FakeHandler : Urbeat.Application.Interfaces.IOutboxEventHandler
    {
        private readonly Func<OutboxMessage, Task<OutboxProcessingResult>> _onHandle;

        public FakeHandler(string supportedType, Func<OutboxMessage, Task<OutboxProcessingResult>> onHandle)
        {
            _onHandle = onHandle;
            SupportedTypes = new[] { supportedType };
        }

        public IReadOnlyCollection<string> SupportedTypes { get; }

        public int Invocations { get; private set; }

        public Task<OutboxProcessingResult> HandleAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            Invocations++;
            return _onHandle(message);
        }
    }
}
