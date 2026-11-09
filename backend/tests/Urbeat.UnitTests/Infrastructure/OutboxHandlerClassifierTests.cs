using FluentAssertions;
using Urbeat.Application.Outbox;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Outbox;
using Urbeat.Infrastructure.Outbox.Handlers;

namespace Urbeat.UnitTests.Infrastructure;

public sealed class OutboxHandlerClassifierTests
{
    private readonly OutboxHandlerClassifier _classifier = new();

    [Theory]
    [InlineData(typeof(NotificationHandler), OutboxHandlerKeys.Notification, OutboxChannel.Business)]
    [InlineData(typeof(OrderSignalRHandler), OutboxHandlerKeys.OrderSignalR, OutboxChannel.Business)]
    [InlineData(typeof(OrderAcceptedForPrintingSignalRHandler), OutboxHandlerKeys.OrderAcceptedForPrintingSignalR, OutboxChannel.Business)]
    [InlineData(typeof(PrintHandler), OutboxHandlerKeys.Print, OutboxChannel.Business)]
    [InlineData(typeof(EmailHandler), OutboxHandlerKeys.Email, OutboxChannel.Communications)]
    public void Classify_ShouldMapProductionHandlers(
        Type handlerType,
        string expectedKey,
        OutboxChannel expectedChannel)
    {
        var descriptor = _classifier.Classify(handlerType);

        descriptor.HandlerKey.Should().Be(expectedKey);
        descriptor.Channel.Should().Be(expectedChannel);
    }

    [Fact]
    public void Classify_ShouldRejectUnregisteredHandler()
    {
        var act = () => _classifier.Classify(typeof(UnclassifiedHandler));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*has no channel classification*");
    }

    private sealed class UnclassifiedHandler : Urbeat.Application.Interfaces.IOutboxEventHandler
    {
        public IReadOnlyCollection<string> SupportedTypes => new[] { "Unknown" };

        public Task<OutboxProcessingResult> HandleAsync(
            OutboxMessage message,
            CancellationToken cancellationToken = default)
            => Task.FromResult(OutboxProcessingResult.Ok());
    }
}
