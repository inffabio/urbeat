using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Urbeat.WebApi.Hubs;
using Urbeat.WebApi.Services;

namespace Urbeat.UnitTests.WebApi;

public sealed class SignalRLiveNotificationPublisherTests
{
    [Fact]
    public async Task PublishSellerAsync_ShouldSendEventToSellerUser()
    {
        var sellerUserId = Guid.NewGuid();
        object?[]? capturedArgs = null;
        var proxy = new Mock<ISingleClientProxy>();
        proxy
            .Setup(x => x.SendCoreAsync("ReceiveSellerNotification", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object?[], CancellationToken>((_, args, _) => capturedArgs = args)
            .Returns(Task.CompletedTask);
        var clients = new Mock<IHubClients>();
        clients.Setup(x => x.User(sellerUserId.ToString())).Returns(proxy.Object);
        var sellerHub = new Mock<IHubContext<SellerNotificationHub>>();
        sellerHub.SetupGet(x => x.Clients).Returns(clients.Object);

        var sut = new SignalRLiveNotificationPublisher(
            sellerHub.Object,
            new Mock<IHubContext<CustomerNotificationHub>>().Object,
            NullLogger<SignalRLiveNotificationPublisher>.Instance);

        var payload = new { orderId = Guid.NewGuid() };
        var delivered = await sut.PublishSellerAsync(sellerUserId, "ReceiveSellerNotification", payload);

        delivered.Should().BeTrue();
        capturedArgs.Should().ContainSingle().Which.Should().BeSameAs(payload);
        clients.Verify(x => x.User(sellerUserId.ToString()), Times.Once);
    }

    [Fact]
    public async Task PublishCustomerAsync_ShouldSendEventToCustomerUser()
    {
        var customerUserId = Guid.NewGuid();
        var proxy = new Mock<ISingleClientProxy>();
        proxy
            .Setup(x => x.SendCoreAsync("OrderStatusUpdated", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var clients = new Mock<IHubClients>();
        clients.Setup(x => x.User(customerUserId.ToString())).Returns(proxy.Object);
        var customerHub = new Mock<IHubContext<CustomerNotificationHub>>();
        customerHub.SetupGet(x => x.Clients).Returns(clients.Object);

        var sut = new SignalRLiveNotificationPublisher(
            new Mock<IHubContext<SellerNotificationHub>>().Object,
            customerHub.Object,
            NullLogger<SignalRLiveNotificationPublisher>.Instance);

        var delivered = await sut.PublishCustomerAsync(customerUserId, "OrderStatusUpdated", new { status = 4 });

        delivered.Should().BeTrue();
        proxy.Verify(
            x => x.SendCoreAsync("OrderStatusUpdated", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()),
            Times.Once);
        clients.Verify(x => x.User(customerUserId.ToString()), Times.Once);
    }

    [Fact]
    public async Task PublishAsync_ShouldReturnFalse_AndLogWithoutSensitiveDetails_WhenTransportFails()
    {
        const string sensitiveMarker = "payload-must-not-be-logged";
        var customerUserId = Guid.NewGuid();
        var proxy = new Mock<ISingleClientProxy>();
        proxy
            .Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(
                "UnableToConnect on redis://:redis-secret@redis-prod:6379,password=hunter2 customer@example.com"));
        var clients = new Mock<IHubClients>();
        clients.Setup(x => x.User(It.IsAny<string>())).Returns(proxy.Object);
        var customerHub = new Mock<IHubContext<CustomerNotificationHub>>();
        customerHub.SetupGet(x => x.Clients).Returns(clients.Object);
        var logger = new CapturingLogger<SignalRLiveNotificationPublisher>();

        var sut = new SignalRLiveNotificationPublisher(
            new Mock<IHubContext<SellerNotificationHub>>().Object,
            customerHub.Object,
            logger);

        var delivered = await sut.PublishCustomerAsync(
            customerUserId,
            "OrderStatusUpdated",
            new { secret = sensitiveMarker });

        delivered.Should().BeFalse();
        var warning = logger.Entries.Should().ContainSingle().Subject;
        warning.Level.Should().Be(LogLevel.Warning);
        warning.Message.Should().Contain("SignalR live push failed");
        warning.Message.Should().Contain("OrderStatusUpdated");
        warning.Message.Should().Contain(nameof(InvalidOperationException));
        warning.Message.Should().Contain(customerUserId.ToString());
        warning.Message.Should().NotContain(sensitiveMarker);
        // Neither the payload nor the transport exception text may reach the log: it can embed
        // connection strings and personal data.
        warning.Message.Should().NotContain("redis-secret");
        warning.Message.Should().NotContain("hunter2");
        warning.Message.Should().NotContain("redis-prod");
        warning.Message.Should().NotContain("customer@example.com");
        warning.Message.Should().NotContain("UnableToConnect");
        warning.Exception.Should().BeNull();
    }

    [Fact]
    public async Task PublishAsync_ShouldRethrow_WhenCallerCancels()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var proxy = new Mock<ISingleClientProxy>();
        proxy
            .Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));
        var clients = new Mock<IHubClients>();
        clients.Setup(x => x.User(It.IsAny<string>())).Returns(proxy.Object);
        var sellerHub = new Mock<IHubContext<SellerNotificationHub>>();
        sellerHub.SetupGet(x => x.Clients).Returns(clients.Object);

        var sut = new SignalRLiveNotificationPublisher(
            sellerHub.Object,
            new Mock<IHubContext<CustomerNotificationHub>>().Object,
            NullLogger<SignalRLiveNotificationPublisher>.Instance);

        var act = () => sut.PublishSellerAsync(Guid.NewGuid(), "ReceiveSellerNotification", new { }, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task PublishAsync_ShouldReturnFalse_WhenRecipientIsMissing()
    {
        var sut = new SignalRLiveNotificationPublisher(
            new Mock<IHubContext<SellerNotificationHub>>().Object,
            new Mock<IHubContext<CustomerNotificationHub>>().Object,
            NullLogger<SignalRLiveNotificationPublisher>.Instance);

        var delivered = await sut.PublishSellerAsync(Guid.Empty, "ReceiveSellerNotification", new { });

        delivered.Should().BeFalse();
    }

    /// <summary>
    /// Regression for the production failure: the concrete <c>IHubContext&lt;THub&gt;</c> type
    /// registered by SignalR is internal, so the previous Infrastructure implementation that
    /// resolved it by reflection and invoked it through <c>dynamic</c> threw
    /// <c>RuntimeBinderException</c> on every push. The typed publisher must send through the public
    /// interface against the real SignalR registrations.
    /// </summary>
    [Fact]
    public async Task PublishAsync_ShouldSucceed_WithRealSignalRHubContexts()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSignalR();
        await using var provider = services.BuildServiceProvider();

        var sut = new SignalRLiveNotificationPublisher(
            provider.GetRequiredService<IHubContext<SellerNotificationHub>>(),
            provider.GetRequiredService<IHubContext<CustomerNotificationHub>>(),
            NullLogger<SignalRLiveNotificationPublisher>.Instance);

        var sellerDelivered = await sut.PublishSellerAsync(Guid.NewGuid(), "ReceiveSellerNotification", new { orderId = Guid.NewGuid() });
        var customerDelivered = await sut.PublishCustomerAsync(Guid.NewGuid(), "OrderStatusUpdated", new { orderId = Guid.NewGuid() });

        sellerDelivered.Should().BeTrue();
        customerDelivered.Should().BeTrue();
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);
}
