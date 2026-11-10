using FluentAssertions;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.WebApi.Operations;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.UnitTests.WebApi;

public sealed class OutboxOperationsSnapshotReaderTests
{
    [Fact]
    public async Task ReadAsync_ShouldProjectCountsOldestBacklogAndUtcAges()
    {
        var now = new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

        using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

        var message = new OutboxMessage
        {
            Type = "OrderCreated",
            Payload = "{\"sensitive\":true}",
            OccurredAtUtc = now.AddMinutes(-40).UtcDateTime,
            AvailableAtUtc = now.AddMinutes(-20).UtcDateTime,
            Status = OutboxMessageStatus.Pending
        };

        db.OutboxMessages.Add(message);
        db.OutboxHandlerDeliveries.AddRange(
            new OutboxHandlerDelivery
            {
                OutboxMessageId = message.Id,
                HandlerKey = "business.notification",
                Channel = OutboxChannel.Business,
                Status = OutboxHandlerDeliveryStatus.Pending,
                CreatedAtUtc = now.AddMinutes(-40).UtcDateTime,
                AvailableAtUtc = now.AddMinutes(-10).UtcDateTime
            },
            new OutboxHandlerDelivery
            {
                OutboxMessageId = message.Id,
                HandlerKey = "business.print",
                Channel = OutboxChannel.Business,
                Status = OutboxHandlerDeliveryStatus.Processing,
                CreatedAtUtc = now.AddMinutes(-20).UtcDateTime,
                AvailableAtUtc = now.AddMinutes(-5).UtcDateTime
            },
            new OutboxHandlerDelivery
            {
                OutboxMessageId = message.Id,
                HandlerKey = "communications.email",
                Channel = OutboxChannel.Communications,
                Status = OutboxHandlerDeliveryStatus.Failed,
                CreatedAtUtc = now.AddMinutes(-5).UtcDateTime,
                AvailableAtUtc = now.AddMinutes(-5).UtcDateTime
            });

        await db.SaveChangesAsync();

        var reader = new OutboxOperationsSnapshotReader(db, new ManualTimeProvider(now));

        var snapshot = await reader.ReadAsync(CancellationToken.None);

        snapshot.Business.Pending.Should().Be(1);
        snapshot.Business.Processing.Should().Be(1);
        snapshot.Business.OldestBacklogAgeSeconds.Should().Be(600);
        snapshot.Business.OldestBacklogAtUtc.Should().Be(now.AddMinutes(-10).UtcDateTime);

        snapshot.Communications.Failed.Should().Be(1);
        snapshot.Communications.Pending.Should().Be(0);
        snapshot.Communications.OldestBacklogAgeSeconds.Should().BeNull();

        snapshot.Totals.Pending.Should().Be(1);
        snapshot.Totals.Processing.Should().Be(1);
        snapshot.Totals.OldestBacklogAgeSeconds.Should().Be(600);

        snapshot.Messages.Pending.Should().Be(1);
        snapshot.Messages.Failed.Should().Be(0);
        snapshot.Messages.OldestBacklogAgeSeconds.Should().Be(1200);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        public ManualTimeProvider(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
