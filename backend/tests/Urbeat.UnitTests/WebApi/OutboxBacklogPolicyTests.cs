using FluentAssertions;
using Urbeat.Domain.Entities;
using Urbeat.WebApi.Operations;

namespace Urbeat.UnitTests.WebApi;

public sealed class OutboxBacklogPolicyTests
{
    [Theory]
    [InlineData(OutboxMessageStatus.Pending, true)]
    [InlineData(OutboxMessageStatus.Processing, true)]
    [InlineData(OutboxMessageStatus.Processed, false)]
    [InlineData(OutboxMessageStatus.Failed, false)]
    public void MessageStatus_ShouldUsePendingAndProcessingAsBacklog(
        OutboxMessageStatus status,
        bool expected)
        => OutboxBacklogPolicy.IsBacklog(status).Should().Be(expected);

    [Theory]
    [InlineData(OutboxHandlerDeliveryStatus.Pending, true)]
    [InlineData(OutboxHandlerDeliveryStatus.Processing, true)]
    [InlineData(OutboxHandlerDeliveryStatus.Processed, false)]
    [InlineData(OutboxHandlerDeliveryStatus.Failed, false)]
    public void DeliveryStatus_ShouldUsePendingAndProcessingAsBacklog(
        OutboxHandlerDeliveryStatus status,
        bool expected)
        => OutboxBacklogPolicy.IsBacklog(status).Should().Be(expected);
}
