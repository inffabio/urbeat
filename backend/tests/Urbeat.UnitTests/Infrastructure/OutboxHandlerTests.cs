using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Outbox;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Outbox;
using Urbeat.Infrastructure.Outbox.Handlers;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Services.Email;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Urbeat.UnitTests.Infrastructure;

public sealed class OutboxHandlerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private static OutboxMessage MessageOf(string type, object payload) => new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        AggregateId = Guid.NewGuid(),
        Payload = JsonSerializer.Serialize(payload, JsonOptions)
    };

    private static (ApplicationDbContext Db, IOutboxDeliveryTracker Tracker) CreateTracker()
    {
        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"urbeat-handler-{Guid.NewGuid()}")
                .Options);
        return (db, new OutboxDeliveryTracker(db));
    }

    private static Mock<UserManager<IdentityUser<Guid>>> CreateUserManagerMock()
    {
        var store = new Mock<IUserStore<IdentityUser<Guid>>>();
        return new Mock<UserManager<IdentityUser<Guid>>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
    }

    [Fact]
    public async Task OrderSignalRHandler_ShouldPublishOrderStatusUpdated_ForSellerDrivenChanges()
    {
        var notificationService = new Mock<INotificationService>();
        notificationService
            .Setup(x => x.NotifyCustomerOrderStatusUpdatedAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<OrderStatus>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var (db, _) = CreateTracker();
        var handler = new OrderSignalRHandler(notificationService.Object);
        var orderId = Guid.NewGuid();
        var customerUserId = Guid.NewGuid();
        var changedAtUtc = new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc);

        var message = MessageOf(OutboxEventTypes.OrderStatusChanged, new OrderStatusChangedEvent
        {
            OrderId = orderId,
            CustomerUserId = customerUserId,
            Code = "URB-123456",
            NewStatus = OrderStatus.Preparing,
            ChangedAtUtc = changedAtUtc,
            Source = "Seller"
        });

        var result = await handler.HandleAsync(message);

        result.Success.Should().BeTrue();
        notificationService.Verify(
            x => x.NotifyCustomerOrderStatusUpdatedAsync(customerUserId, orderId, "URB-123456", OrderStatus.Preparing, changedAtUtc, It.IsAny<CancellationToken>()),
            Times.Once);
        // SignalR is at-least-once; no delivery marker is recorded for live pushes.
        db.OutboxDeliveries.Local.Should().BeEmpty();
    }

    [Fact]
    public async Task OrderSignalRHandler_ShouldReturnRetry_WhenSignalRPushFails()
    {
        var notificationService = new Mock<INotificationService>();
        notificationService
            .Setup(x => x.NotifyCustomerOrderStatusUpdatedAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<OrderStatus>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var handler = new OrderSignalRHandler(notificationService.Object);

        var message = MessageOf(OutboxEventTypes.OrderStatusChanged, new OrderStatusChangedEvent
        {
            OrderId = Guid.NewGuid(),
            NewStatus = OrderStatus.Received,
            Source = "Seller"
        });

        var result = await handler.HandleAsync(message);

        result.Success.Should().BeFalse();
        result.Retryable.Should().BeTrue();
    }

    [Fact]
    public async Task OrderSignalRHandler_ShouldIgnoreWebhookDrivenChanges()
    {
        var notificationService = new Mock<INotificationService>();
        var handler = new OrderSignalRHandler(notificationService.Object);

        var message = MessageOf(OutboxEventTypes.OrderStatusChanged, new OrderStatusChangedEvent
        {
            OrderId = Guid.NewGuid(),
            NewStatus = OrderStatus.Received,
            Source = "Webhook"
        });

        var result = await handler.HandleAsync(message);

        result.Success.Should().BeTrue();
        notificationService.Verify(
            x => x.NotifyCustomerOrderStatusUpdatedAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<OrderStatus>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task NotificationHandler_ShouldPushCustomerAndSellerNotifications()
    {
        await using var db = CreateDbContext();
        var customerUserId = Guid.NewGuid();
        var sellerUserId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        db.Notifications.AddRange(
            new Notification { RecipientUserId = sellerUserId, OrderId = orderId, Type = NotificationType.NewOrder, Title = "Novo pedido", Message = "novo" },
            new Notification { RecipientUserId = customerUserId, OrderId = orderId, Type = NotificationType.OrderReceived, Title = "Recebido", Message = "ok" });
        await db.SaveChangesAsync();

        var notificationService = new Mock<INotificationService>();
        notificationService
            .Setup(x => x.PushSellerNotificationAsync(It.IsAny<Guid>(), It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        notificationService
            .Setup(x => x.PushCustomerNotificationAsync(It.IsAny<Guid>(), It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = new NotificationHandler(notificationService.Object, db);

        var message = MessageOf(OutboxEventTypes.OrderStatusChanged, new OrderStatusChangedEvent
        {
            OrderId = orderId,
            CustomerUserId = customerUserId,
            SellerUserId = sellerUserId,
            NewStatus = OrderStatus.Received
        });

        var result = await handler.HandleAsync(message);

        result.Success.Should().BeTrue();
        notificationService.Verify(
            x => x.PushSellerNotificationAsync(sellerUserId, It.Is<Notification>(n => n.Type == NotificationType.NewOrder), It.IsAny<CancellationToken>()),
            Times.Once);
        notificationService.Verify(
            x => x.PushCustomerNotificationAsync(customerUserId, It.Is<Notification>(n => n.Type == NotificationType.OrderReceived), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task EmailHandler_ShouldSendEmail_ForEmailChannel()
    {
        var emailService = new Mock<IEmailService>();
        emailService.Setup(x => x.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        var (_, tracker) = CreateTracker();
        var handler = CreateEmailHandler(emailService.Object, tracker);

        var message = MessageOf(OutboxEventTypes.OutboundMessageRequested, new OutboundMessageRequestedEvent
        {
            Channel = "Email",
            DeliveryKey = "email-confirm:1",
            Recipient = "user@urbeat.local",
            ToName = "User",
            Subject = "Confirme",
            Body = "<html/>"
        });

        var result = await handler.HandleAsync(message);

        result.Success.Should().BeTrue();
        emailService.Verify(
            x => x.SendAsync("user@urbeat.local", "User", "Confirme", "<html/>", null, "email-confirm:1", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task EmailHandler_ShouldRenderConfirmationContent_AtSendTime_FromSafeReference()
    {
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var tokenCache = new Mock<IEmailTokenCache>();
        tokenCache.Setup(x => x.GetConfirmationRequestAsync(correlationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailConfirmationRequest
            {
                CorrelationId = correlationId,
                UserId = userId,
                ShortCode = "ABC123XYZ",
                Token = "enc-token"
            });

        var userManager = CreateUserManagerMock();
        userManager.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(new IdentityUser<Guid> { Id = userId, Email = "user@urbeat.local", UserName = "user@urbeat.local" });

        var emailService = new Mock<IEmailService>();
        emailService.Setup(x => x.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("provider-msg-id");

        var (db, tracker) = CreateTracker();
        var handler = CreateEmailHandler(emailService.Object, tracker, tokenCache.Object, userManager.Object);

        var message = MessageOf(OutboxEventTypes.OutboundMessageRequested, new OutboundMessageRequestedEvent
        {
            Channel = "Email",
            DeliveryKey = "email-confirm:req-1",
            Template = "CustomerConfirmation",
            CorrelationId = correlationId,
            UserId = userId
        });

        var result = await handler.HandleAsync(message);

        result.Success.Should().BeTrue();
        emailService.Verify(x => x.SendAsync(
            "user@urbeat.local",
            "user@urbeat.local",
            It.Is<string>(s => s.Contains("Confirme")),
            It.Is<string>(b => b.Contains("/c/ABC123XYZ")),
            null,
            "email-confirm:req-1",
            It.IsAny<CancellationToken>()), Times.Once);

        db.OutboxDeliveries.Local.Should().ContainSingle(x => x.DeliveryKey == "email-confirm:req-1");
        db.OutboxDeliveries.Local.Single().ProviderMessageId.Should().Be("provider-msg-id");
    }

    [Fact]
    public async Task EmailHandler_ShouldFail_WhenConfirmationReferenceIsMissing()
    {
        var tokenCache = new Mock<IEmailTokenCache>();
        tokenCache.Setup(x => x.GetConfirmationRequestAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmailConfirmationRequest?)null);
        var userManager = CreateUserManagerMock();
        var emailService = new Mock<IEmailService>();
        var (_, tracker) = CreateTracker();
        var handler = CreateEmailHandler(emailService.Object, tracker, tokenCache.Object, userManager.Object);

        var message = MessageOf(OutboxEventTypes.OutboundMessageRequested, new OutboundMessageRequestedEvent
        {
            Channel = "Email",
            DeliveryKey = "email-confirm:req-2",
            Template = "CustomerConfirmation",
            CorrelationId = Guid.NewGuid(),
            UserId = Guid.NewGuid()
        });

        var result = await handler.HandleAsync(message);

        result.Success.Should().BeFalse();
        result.Retryable.Should().BeFalse();
        emailService.Verify(x => x.SendAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EmailHandler_ShouldSkipSend_WhenAlreadyDelivered()
    {
        var emailService = new Mock<IEmailService>();
        var (db, tracker) = CreateTracker();
        db.OutboxDeliveries.Add(new OutboxDelivery
        {
            OutboxMessageId = Guid.NewGuid(),
            DeliveryKey = "email-confirm:1",
            HandlerType = nameof(EmailHandler),
            DeliveredAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var handler = CreateEmailHandler(emailService.Object, tracker);

        var message = MessageOf(OutboxEventTypes.OutboundMessageRequested, new OutboundMessageRequestedEvent
        {
            Channel = "Email",
            DeliveryKey = "email-confirm:1",
            Recipient = "user@urbeat.local",
            Subject = "Confirme",
            Body = "<html/>"
        });

        var result = await handler.HandleAsync(message);

        result.Success.Should().BeTrue();
        emailService.Verify(x => x.SendAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EmailHandler_ShouldFailExplicitly_ForUnknownChannel()
    {
        var emailService = new Mock<IEmailService>();
        var (_, tracker) = CreateTracker();
        var handler = CreateEmailHandler(emailService.Object, tracker);

        var message = MessageOf(OutboxEventTypes.OutboundMessageRequested, new OutboundMessageRequestedEvent
        {
            Channel = "Sms",
            Recipient = "5511999999999",
            Body = "code"
        });

        var result = await handler.HandleAsync(message);

        result.Success.Should().BeFalse();
        result.Retryable.Should().BeFalse();
        result.Error.Should().Contain("Sms");
        emailService.Verify(x => x.SendAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EmailHandler_ShouldReturnRetryable_WhenProviderFails()
    {
        var emailService = new Mock<IEmailService>();
        emailService.Setup(x => x.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("smtp down"));
        var (_, tracker) = CreateTracker();
        var handler = CreateEmailHandler(emailService.Object, tracker);

        var message = MessageOf(OutboxEventTypes.OutboundMessageRequested, new OutboundMessageRequestedEvent
        {
            Channel = "Email",
            DeliveryKey = "email-confirm:2",
            Recipient = "user@urbeat.local",
            Subject = "Confirme",
            Body = "<html/>"
        });

        var result = await handler.HandleAsync(message);

        result.Success.Should().BeFalse();
        result.Retryable.Should().BeTrue();
    }

    [Fact]
    public async Task PrintHandler_ShouldReturnExplicitFailure_ForOrderCreated()
    {
        var handler = new PrintHandler(NullLogger<PrintHandler>.Instance);
        var message = MessageOf(OutboxEventTypes.OrderCreated, new OrderCreatedEvent
        {
            OrderId = Guid.NewGuid(),
            Code = "URB-123456"
        });

        var result = await handler.HandleAsync(message);

        result.Success.Should().BeFalse();
        result.Retryable.Should().BeFalse();
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    private static EmailHandler CreateEmailHandler(
        IEmailService emailService,
        IOutboxDeliveryTracker tracker,
        IEmailTokenCache? tokenCache = null,
        UserManager<IdentityUser<Guid>>? userManager = null)
    {
        return new EmailHandler(
            emailService,
            tracker,
            tokenCache ?? new Mock<IEmailTokenCache>().Object,
            userManager ?? CreateUserManagerMock().Object,
            Options.Create(new EmailConfirmationOptions { FrontendBaseUrl = "https://app.urbeat.test" }));
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-handler-{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }
}
