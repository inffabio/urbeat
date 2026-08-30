using FluentAssertions;
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
/// Verifies the PostgreSQL-specific outbox claim (UPDATE ... FOR UPDATE SKIP LOCKED ... RETURNING)
/// against a real database. The InMemory provider used by the rest of the suite cannot execute the
/// row-locking claim, so this test runs against a Testcontainers PostgreSQL container and is
/// skipped when Docker is unavailable.
/// </summary>
public sealed class OutboxDispatcherRelationalTests
{
    private readonly ITestOutputHelper _output;

    public OutboxDispatcherRelationalTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task DispatchBatchAsync_ShouldClaimAndProcess_PostgreSqlSkipLocked()
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

            var dispatcher = new OutboxDispatcher(
                setupContext,
                new[] { new NoopHandler("OrderCreated") },
                Options.Create(new OutboxOptions { BatchSize = 10, MaxAttempts = 3 }),
                NullLogger<OutboxDispatcher>.Instance);

            var processed = await dispatcher.DispatchBatchAsync();

            processed.Should().Be(3);

            await using var verifyContext = new ApplicationDbContext(options);
            (await verifyContext.OutboxMessages.CountAsync(x => x.Status == OutboxMessageStatus.Processed)).Should().Be(3);
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

    private sealed class NoopHandler : Urbeat.Application.Interfaces.IOutboxEventHandler
    {
        public NoopHandler(string supportedType)
        {
            SupportedTypes = new[] { supportedType };
        }

        public IReadOnlyCollection<string> SupportedTypes { get; }

        public Task<OutboxProcessingResult> HandleAsync(OutboxMessage message, CancellationToken cancellationToken = default)
            => Task.FromResult(OutboxProcessingResult.Ok());
    }
}
