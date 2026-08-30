using FluentAssertions;
using Urbeat.Domain.Entities;

namespace Urbeat.UnitTests.Infrastructure;

public sealed class OutboxMessageTests
{
    [Fact]
    public void NewMessage_ShouldDefaultToPending_WithUtcOccurrence()
    {
        var message = new OutboxMessage
        {
            Type = "OrderCreated",
            AggregateId = Guid.NewGuid(),
            OccurredAtUtc = DateTime.UtcNow
        };

        message.Id.Should().NotBe(Guid.Empty);
        message.Status.Should().Be(OutboxMessageStatus.Pending);
        message.AttemptCount.Should().Be(0);
        message.LockedUntilUtc.Should().BeNull();
        message.ProcessedAtUtc.Should().BeNull();
        message.LastError.Should().BeNull();
        message.OccurredAtUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void MarkProcessing_ShouldSetStatusAndLock()
    {
        var message = new OutboxMessage { Type = "OrderCreated" };
        var lockUntil = DateTime.UtcNow.AddSeconds(30);

        message.Status = OutboxMessageStatus.Processing;
        message.LockedUntilUtc = lockUntil;
        message.AttemptCount = 1;

        message.Status.Should().Be(OutboxMessageStatus.Processing);
        message.LockedUntilUtc.Should().Be(lockUntil);
        message.AttemptCount.Should().Be(1);
    }

    [Fact]
    public void MarkProcessed_ShouldSetStatusAndProcessedAtUtc()
    {
        var message = new OutboxMessage { Type = "OrderCreated" };
        var processedAt = DateTime.UtcNow;

        message.Status = OutboxMessageStatus.Processed;
        message.ProcessedAtUtc = processedAt;

        message.Status.Should().Be(OutboxMessageStatus.Processed);
        message.ProcessedAtUtc.Should().Be(processedAt);
        message.ProcessedAtUtc!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void MarkFailed_ShouldSetStatusAndRetainLastError()
    {
        var message = new OutboxMessage { Type = "OrderCreated" };

        message.Status = OutboxMessageStatus.Failed;
        message.LastError = "SMTP unavailable";

        message.Status.Should().Be(OutboxMessageStatus.Failed);
        message.LastError.Should().Be("SMTP unavailable");
    }
}
