using FluentAssertions;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Outbox;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Outbox;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;
using Xunit.Abstractions;

namespace Urbeat.IntegrationTests.Relational;

/// <summary>
/// Verifies the PostgreSQL-specific delivery claim (UPDATE ... FOR UPDATE SKIP LOCKED ... RETURNING)
/// and concurrent fan-out against a real database. The InMemory provider used by the rest of the
/// suite cannot execute row locking, so these tests run against a Testcontainers PostgreSQL
/// container and self-skip when Docker is unavailable.
/// </summary>
public sealed class OutboxDispatcherRelationalTests
{
    private readonly ITestOutputHelper _output;

    public OutboxDispatcherRelationalTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task DispatchBatchAsync_ShouldClaimAndProcess_PostgreSqlSkipLocked_PerChannel()
    {
        var container = await StartPostgresOrSkipAsync();
        if (container is null)
        {
            return;
        }

        try
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(container.GetConnectionString())
                .Options;

            await using var setupContext = new ApplicationDbContext(options);
            await setupContext.Database.EnsureCreatedAsync();

            for (var i = 0; i < 3; i++)
            {
                setupContext.OutboxMessages.Add(new OutboxMessage
                {
                    Type = "OrderCreated",
                    AggregateId = Guid.NewGuid(),
                    Payload = "{}",
                    OccurredAtUtc = DateTime.UtcNow,
                    AvailableAtUtc = DateTime.UtcNow,
                    Status = OutboxMessageStatus.Pending
                });
            }
            await setupContext.SaveChangesAsync();

            var business = new CountingHandler("business.notification", OutboxChannel.Business, "OrderCreated");
            var communications = new CountingHandler("communications.email", OutboxChannel.Communications, "OrderCreated");

            var dispatcher = new OutboxDispatcher(
                setupContext,
                new IOutboxEventHandler[] { business, communications },
                new TestClassifier(),
                Options.Create(new OutboxOptions { BatchSize = 10, MaxAttempts = 3 }),
                NullLogger<OutboxDispatcher>.Instance);

            var processed = await dispatcher.DispatchBatchAsync(OutboxChannel.Business);

            processed.Should().Be(3);
            business.Invocations.Should().Be(3);
            communications.Invocations.Should().Be(0);

            await using var verifyContext = new ApplicationDbContext(options);
            (await verifyContext.OutboxHandlerDeliveries
                    .CountAsync(x => x.Channel == OutboxChannel.Business
                        && x.Status == OutboxHandlerDeliveryStatus.Processed))
                .Should().Be(3);
            (await verifyContext.OutboxHandlerDeliveries
                    .CountAsync(x => x.Channel == OutboxChannel.Communications
                        && x.Status == OutboxHandlerDeliveryStatus.Pending))
                .Should().Be(3);

            // The communications worker drains its own channel independently.
            var communicationsDispatcher = new OutboxDispatcher(
                verifyContext,
                new IOutboxEventHandler[] { business, communications },
                new TestClassifier(),
                Options.Create(new OutboxOptions { BatchSize = 10, MaxAttempts = 3 }),
                NullLogger<OutboxDispatcher>.Instance);

            (await communicationsDispatcher.DispatchBatchAsync(OutboxChannel.Communications)).Should().Be(3);
            communications.Invocations.Should().Be(3);
        }
        finally
        {
            await container.DisposeAsync();
        }
    }

    [Fact]
    public async Task DispatchBatchAsync_ShouldNotProcessTheSameDeliveryTwice_UnderConcurrentClaim()
    {
        var container = await StartPostgresOrSkipAsync();
        if (container is null)
        {
            return;
        }

        try
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(container.GetConnectionString())
                .Options;

            const int messageCount = 20;

            await using (var setupContext = new ApplicationDbContext(options))
            {
                await setupContext.Database.EnsureCreatedAsync();

                for (var i = 0; i < messageCount; i++)
                {
                    setupContext.OutboxMessages.Add(new OutboxMessage
                    {
                        Type = "OrderCreated",
                        AggregateId = Guid.NewGuid(),
                        Payload = "{}",
                        OccurredAtUtc = DateTime.UtcNow,
                        AvailableAtUtc = DateTime.UtcNow,
                        Status = OutboxMessageStatus.Pending
                    });
                }
                await setupContext.SaveChangesAsync();
            }

            var invocations = 0;

            async Task<int> RunDispatcherAsync()
            {
                await using var context = new ApplicationDbContext(options);
                var handler = new CountingHandler(
                    "business.notification",
                    OutboxChannel.Business,
                    "OrderCreated",
                    () => Interlocked.Increment(ref invocations));
                var dispatcher = new OutboxDispatcher(
                    context,
                    new IOutboxEventHandler[] { handler },
                    new TestClassifier(),
                    Options.Create(new OutboxOptions { BatchSize = 5, MaxAttempts = 3 }),
                    NullLogger<OutboxDispatcher>.Instance);

                var processed = 0;
                for (var pass = 0; pass < 10; pass++)
                {
                    var claimed = await dispatcher.DispatchBatchAsync(OutboxChannel.Business);
                    processed += claimed;
                    if (claimed == 0)
                    {
                        break;
                    }
                }

                return processed;
            }

            var first = RunDispatcherAsync();
            var second = RunDispatcherAsync();
            var results = await Task.WhenAll(first, second);

            results.Sum().Should().Be(messageCount);
            invocations.Should().Be(messageCount);

            await using var verifyContext = new ApplicationDbContext(options);
            (await verifyContext.OutboxHandlerDeliveries
                    .CountAsync(x => x.Status == OutboxHandlerDeliveryStatus.Processed))
                .Should().Be(messageCount);
            (await verifyContext.OutboxHandlerDeliveries.CountAsync())
                .Should().Be(messageCount);
        }
        finally
        {
            await container.DisposeAsync();
        }
    }

    private async Task<PostgreSqlContainer?> StartPostgresOrSkipAsync()
    {
        PostgreSqlContainer? container = null;

        try
        {
            container = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .WithDatabase("urbeat")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();

            await container.StartAsync();
            return container;
        }
        catch (Exception exception)
        {
            if (container is not null)
            {
                await container.DisposeAsync();
            }

            _output.WriteLine($"SKIPPED (Docker unavailable): {exception.Message}");
            return null;
        }
    }

    private sealed class TestClassifier : IOutboxHandlerClassifier
    {
        public OutboxHandlerDescriptor Classify(IOutboxEventHandler handler)
        {
            return handler is CountingHandler counting
                ? new OutboxHandlerDescriptor(counting.HandlerKey, counting.Channel)
                : throw new InvalidOperationException($"Unclassified handler {handler.GetType().Name}.");
        }
    }

    private sealed class CountingHandler : IOutboxEventHandler
    {
        private readonly Action? _onHandle;

        public CountingHandler(
            string handlerKey,
            OutboxChannel channel,
            string supportedType,
            Action? onHandle = null)
        {
            HandlerKey = handlerKey;
            Channel = channel;
            SupportedTypes = new[] { supportedType };
            _onHandle = onHandle;
        }

        public string HandlerKey { get; }

        public OutboxChannel Channel { get; }

        public IReadOnlyCollection<string> SupportedTypes { get; }

        public int Invocations { get; private set; }

        public Task<OutboxProcessingResult> HandleAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            Invocations++;
            _onHandle?.Invoke();
            return Task.FromResult(OutboxProcessingResult.Ok());
        }
    }
}
