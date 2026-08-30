using System.Text;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Outbox;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Services.Email;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Urbeat.UnitTests.Infrastructure;

public sealed class EmailConfirmationServiceTests
{
    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-email-tests-{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static Mock<UserManager<IdentityUser<Guid>>> CreateUserManagerMock()
    {
        var storeMock = new Mock<IUserStore<IdentityUser<Guid>>>();
        return new Mock<UserManager<IdentityUser<Guid>>>(
            storeMock.Object, null!, null!, null!, null!, null!, null!, null!, null!);
    }

    private static EmailConfirmationOptions DefaultOptions() => new()
    {
        FrontendBaseUrl = "https://app.urbeat.local",
        ConfirmPath = "/confirm-email"
    };

    private static (Mock<IOutboxWriter> Writer, Func<OutboundMessageRequestedEvent?> GetCaptured) CreateCapturingWriter()
    {
        OutboundMessageRequestedEvent? captured = null;
        var writer = new Mock<IOutboxWriter>();
        writer.Setup(x => x.EnqueueAsync(
                OutboxEventTypes.OutboundMessageRequested,
                It.IsAny<Guid>(),
                It.IsAny<OutboundMessageRequestedEvent>(),
                It.IsAny<DateTime>(),
                It.IsAny<long>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Guid, OutboundMessageRequestedEvent, DateTime, long, string?, CancellationToken>(
                (_, _, evt, _, _, _, _) => captured = evt)
            .Returns(Task.CompletedTask);
        return (writer, () => captured);
    }

    [Fact]
    public async Task SendConfirmationEmailAsync_ShouldDoNothing_WhenUserNotFound()
    {
        var userManagerMock = CreateUserManagerMock();
        userManagerMock.Setup(m => m.FindByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((IdentityUser<Guid>?)null);

        var (writer, _) = CreateCapturingWriter();
        using var db = CreateDbContext();

        var sut = new EmailConfirmationService(
            userManagerMock.Object,
            Options.Create(DefaultOptions()),
            db,
            new Mock<IEmailTokenCache>().Object,
            writer.Object,
            NullLogger<EmailConfirmationService>.Instance);

        await sut.SendConfirmationEmailAsync(Guid.NewGuid());

        writer.Verify(x => x.EnqueueAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<object>(), It.IsAny<DateTime>(), It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        db.AuditLogs.Should().BeEmpty();
    }

    [Fact]
    public async Task SendConfirmationEmailAsync_ShouldSkip_WhenEmailAlreadyConfirmed()
    {
        var user = new IdentityUser<Guid>
        {
            Id = Guid.NewGuid(),
            Email = "user@urbeat.local",
            UserName = "user@urbeat.local",
            EmailConfirmed = true,
        };

        var userManagerMock = CreateUserManagerMock();
        userManagerMock.Setup(m => m.FindByIdAsync(user.Id.ToString()))
            .ReturnsAsync(user);

        var (writer, _) = CreateCapturingWriter();
        using var db = CreateDbContext();

        var sut = new EmailConfirmationService(
            userManagerMock.Object,
            Options.Create(DefaultOptions()),
            db,
            new Mock<IEmailTokenCache>().Object,
            writer.Object,
            NullLogger<EmailConfirmationService>.Instance);

        await sut.SendConfirmationEmailAsync(user.Id);

        writer.Verify(x => x.EnqueueAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<object>(), It.IsAny<DateTime>(), It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SendConfirmationEmailAsync_ShouldEnqueueCustomerTemplate_WhenUserHasCustomerRole()
    {
        var user = new IdentityUser<Guid>
        {
            Id = Guid.NewGuid(),
            Email = "customer@urbeat.local",
            UserName = "customer@urbeat.local",
            EmailConfirmed = false,
        };

        var userManagerMock = CreateUserManagerMock();
        userManagerMock.Setup(m => m.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        userManagerMock.Setup(m => m.GenerateEmailConfirmationTokenAsync(user)).ReturnsAsync("raw-token-123");
        userManagerMock.Setup(m => m.GetRolesAsync(user)).ReturnsAsync(new List<string> { "Customer" });

        var (writer, captured) = CreateCapturingWriter();
        using var db = CreateDbContext();
        var sut = new EmailConfirmationService(
            userManagerMock.Object,
            Options.Create(DefaultOptions()),
            db,
            new Mock<IEmailTokenCache>().Object,
            writer.Object,
            NullLogger<EmailConfirmationService>.Instance);

        await sut.SendConfirmationEmailAsync(user.Id);

        var evt = captured();
        evt.Should().NotBeNull();
        evt!.Channel.Should().Be("Email");
        evt.Template.Should().Be("CustomerConfirmation");
        evt.CorrelationId.Should().NotBeNull();
        evt.UserId.Should().Be(user.Id);
        evt.DeliveryKey.Should().StartWith("email-confirm:");
        // No short code or token may be persisted in the durable payload; content is rendered at send time.
        evt.Recipient.Should().BeNullOrEmpty();
        evt.Subject.Should().BeNullOrEmpty();
        evt.Body.Should().BeNullOrEmpty();

        db.AuditLogs.Should().HaveCount(1);
        db.AuditLogs.Single().Event.Should().Be("EmailConfirmationSent");
    }

    [Fact]
    public async Task SendConfirmationEmailAsync_ShouldEnqueueSellerTemplate_WhenUserHasSellerRole()
    {
        var user = new IdentityUser<Guid>
        {
            Id = Guid.NewGuid(),
            Email = "seller@urbeat.local",
            UserName = "seller@urbeat.local",
            EmailConfirmed = false,
        };

        var userManagerMock = CreateUserManagerMock();
        userManagerMock.Setup(m => m.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        userManagerMock.Setup(m => m.GenerateEmailConfirmationTokenAsync(user)).ReturnsAsync("seller-token");
        userManagerMock.Setup(m => m.GetRolesAsync(user)).ReturnsAsync(new List<string> { "Seller" });

        var (writer, captured) = CreateCapturingWriter();
        using var db = CreateDbContext();
        var sut = new EmailConfirmationService(
            userManagerMock.Object,
            Options.Create(DefaultOptions()),
            db,
            new Mock<IEmailTokenCache>().Object,
            writer.Object,
            NullLogger<EmailConfirmationService>.Instance);

        await sut.SendConfirmationEmailAsync(user.Id);

        var evt = captured();
        evt.Should().NotBeNull();
        evt!.Template.Should().Be("SellerConfirmation");
        evt.CorrelationId.Should().NotBeNull();
        evt.Body.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task ConfirmAsync_ShouldReturnUserNotFound_WhenUserDoesNotExist()
    {
        var userManagerMock = CreateUserManagerMock();
        userManagerMock.Setup(m => m.FindByIdAsync(It.IsAny<string>())).ReturnsAsync((IdentityUser<Guid>?)null);

        using var db = CreateDbContext();

        var sut = new EmailConfirmationService(
            userManagerMock.Object,
            Options.Create(DefaultOptions()),
            db,
            new Mock<IEmailTokenCache>().Object,
            new Mock<IOutboxWriter>().Object,
            NullLogger<EmailConfirmationService>.Instance);

        var result = await sut.ConfirmAsync(new ConfirmEmailRequestDto
        {
            UserId = Guid.NewGuid(),
            Token = "anything"
        });

        result.UserNotFound.Should().BeTrue();
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task ConfirmAsync_ShouldReturnAlreadyConfirmed_WhenEmailIsAlreadyConfirmed()
    {
        var user = new IdentityUser<Guid>
        {
            Id = Guid.NewGuid(),
            Email = "already@urbeat.local",
            EmailConfirmed = true,
        };

        var userManagerMock = CreateUserManagerMock();
        userManagerMock.Setup(m => m.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);

        using var db = CreateDbContext();

        var sut = new EmailConfirmationService(
            userManagerMock.Object,
            Options.Create(DefaultOptions()),
            db,
            new Mock<IEmailTokenCache>().Object,
            new Mock<IOutboxWriter>().Object,
            NullLogger<EmailConfirmationService>.Instance);

        var result = await sut.ConfirmAsync(new ConfirmEmailRequestDto
        {
            UserId = user.Id,
            Token = "anything"
        });

        result.Succeeded.Should().BeTrue();
        result.AlreadyConfirmed.Should().BeTrue();
        userManagerMock.Verify(m => m.ConfirmEmailAsync(It.IsAny<IdentityUser<Guid>>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ConfirmAsync_ShouldReturnInvalidToken_WhenTokenIsNotBase64Url()
    {
        var user = new IdentityUser<Guid>
        {
            Id = Guid.NewGuid(),
            Email = "bad@urbeat.local",
            EmailConfirmed = false,
        };

        var userManagerMock = CreateUserManagerMock();
        userManagerMock.Setup(m => m.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);

        using var db = CreateDbContext();

        var sut = new EmailConfirmationService(
            userManagerMock.Object,
            Options.Create(DefaultOptions()),
            db,
            new Mock<IEmailTokenCache>().Object,
            new Mock<IOutboxWriter>().Object,
            NullLogger<EmailConfirmationService>.Instance);

        var result = await sut.ConfirmAsync(new ConfirmEmailRequestDto
        {
            UserId = user.Id,
            Token = "!!!not-base64!!!"
        });

        result.InvalidToken.Should().BeTrue();
        result.Errors.Should().NotBeEmpty();
        userManagerMock.Verify(m => m.ConfirmEmailAsync(It.IsAny<IdentityUser<Guid>>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ConfirmAsync_ShouldReturnInvalidToken_WhenIdentityRejectsToken()
    {
        var user = new IdentityUser<Guid>
        {
            Id = Guid.NewGuid(),
            Email = "wrong@urbeat.local",
            EmailConfirmed = false,
        };

        var userManagerMock = CreateUserManagerMock();
        userManagerMock.Setup(m => m.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        userManagerMock.Setup(m => m.ConfirmEmailAsync(user, It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Code = "InvalidToken", Description = "Invalid token." }));

        using var db = CreateDbContext();

        var sut = new EmailConfirmationService(
            userManagerMock.Object,
            Options.Create(DefaultOptions()),
            db,
            new Mock<IEmailTokenCache>().Object,
            new Mock<IOutboxWriter>().Object,
            NullLogger<EmailConfirmationService>.Instance);

        var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes("expired-token"));
        var result = await sut.ConfirmAsync(new ConfirmEmailRequestDto
        {
            UserId = user.Id,
            Token = encoded
        });

        result.InvalidToken.Should().BeTrue();
        result.Errors.Should().Contain("Invalid token.");
        db.AuditLogs.Should().Contain(log => log.Event == "EmailConfirmationFailed");
    }

    [Fact]
    public async Task ConfirmAsync_ShouldSucceed_WhenIdentityAcceptsToken()
    {
        var user = new IdentityUser<Guid>
        {
            Id = Guid.NewGuid(),
            Email = "ok@urbeat.local",
            EmailConfirmed = false,
        };

        var userManagerMock = CreateUserManagerMock();
        userManagerMock.Setup(m => m.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        userManagerMock.Setup(m => m.ConfirmEmailAsync(user, "valid-token"))
            .ReturnsAsync(IdentityResult.Success);

        using var db = CreateDbContext();

        var sut = new EmailConfirmationService(
            userManagerMock.Object,
            Options.Create(DefaultOptions()),
            db,
            new Mock<IEmailTokenCache>().Object,
            new Mock<IOutboxWriter>().Object,
            NullLogger<EmailConfirmationService>.Instance);

        var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes("valid-token"));
        var result = await sut.ConfirmAsync(new ConfirmEmailRequestDto
        {
            UserId = user.Id,
            Token = encoded
        });

        result.Succeeded.Should().BeTrue();
        result.AlreadyConfirmed.Should().BeFalse();
        db.AuditLogs.Should().Contain(log => log.Event == "EmailConfirmed");
    }

    [Fact]
    public async Task ResendAsync_ShouldReturnSuccessWithoutSendingEmail_WhenUserNotFound()
    {
        var userManagerMock = CreateUserManagerMock();
        userManagerMock.Setup(m => m.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((IdentityUser<Guid>?)null);

        var (writer, _) = CreateCapturingWriter();
        using var db = CreateDbContext();

        var sut = new EmailConfirmationService(
            userManagerMock.Object,
            Options.Create(DefaultOptions()),
            db,
            new Mock<IEmailTokenCache>().Object,
            writer.Object,
            NullLogger<EmailConfirmationService>.Instance);

        var result = await sut.ResendAsync(new ResendEmailConfirmationRequestDto { Email = "ghost@urbeat.local" });

        result.Succeeded.Should().BeTrue();
        result.AlreadyConfirmed.Should().BeFalse();
        writer.Verify(x => x.EnqueueAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<object>(), It.IsAny<DateTime>(), It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ResendAsync_ShouldReturnAlreadyConfirmed_WhenEmailIsConfirmed()
    {
        var user = new IdentityUser<Guid>
        {
            Id = Guid.NewGuid(),
            Email = "confirmed@urbeat.local",
            EmailConfirmed = true,
        };

        var userManagerMock = CreateUserManagerMock();
        userManagerMock.Setup(m => m.FindByEmailAsync("confirmed@urbeat.local")).ReturnsAsync(user);

        var (writer, _) = CreateCapturingWriter();
        using var db = CreateDbContext();

        var sut = new EmailConfirmationService(
            userManagerMock.Object,
            Options.Create(DefaultOptions()),
            db,
            new Mock<IEmailTokenCache>().Object,
            writer.Object,
            NullLogger<EmailConfirmationService>.Instance);

        var result = await sut.ResendAsync(new ResendEmailConfirmationRequestDto { Email = "CONFIRMED@urbeat.local" });

        result.Succeeded.Should().BeTrue();
        result.AlreadyConfirmed.Should().BeTrue();
        writer.Verify(x => x.EnqueueAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<object>(), It.IsAny<DateTime>(), It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ResendAsync_ShouldTriggerSendConfirmationEmail_WhenUserExistsAndNotConfirmed()
    {
        var user = new IdentityUser<Guid>
        {
            Id = Guid.NewGuid(),
            Email = "pending@urbeat.local",
            UserName = "pending@urbeat.local",
            EmailConfirmed = false,
        };

        var userManagerMock = CreateUserManagerMock();
        userManagerMock.Setup(m => m.FindByEmailAsync("pending@urbeat.local")).ReturnsAsync(user);
        userManagerMock.Setup(m => m.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        userManagerMock.Setup(m => m.GenerateEmailConfirmationTokenAsync(user)).ReturnsAsync("resend-token");
        userManagerMock.Setup(m => m.GetRolesAsync(user)).ReturnsAsync(new List<string> { "Customer" });

        var (writer, captured) = CreateCapturingWriter();
        using var db = CreateDbContext();
        var sut = new EmailConfirmationService(
            userManagerMock.Object,
            Options.Create(DefaultOptions()),
            db,
            new Mock<IEmailTokenCache>().Object,
            writer.Object,
            NullLogger<EmailConfirmationService>.Instance);

        var result = await sut.ResendAsync(new ResendEmailConfirmationRequestDto { Email = "Pending@Urbeat.Local" });

        result.Succeeded.Should().BeTrue();
        result.AlreadyConfirmed.Should().BeFalse();
        var evt = captured();
        evt.Should().NotBeNull();
        evt!.UserId.Should().Be(user.Id);
        db.AuditLogs.Should().Contain(log => log.Event == "EmailConfirmationResent");
        db.AuditLogs.Should().Contain(log => log.Event == "EmailConfirmationSent");
    }

    [Fact]
    public async Task ResendAsync_ShouldUseDistinctDeliveryKey_PerRequest()
    {
        var user = new IdentityUser<Guid>
        {
            Id = Guid.NewGuid(),
            Email = "resend-key@urbeat.local",
            UserName = "resend-key@urbeat.local",
            EmailConfirmed = false,
        };

        var userManagerMock = CreateUserManagerMock();
        userManagerMock.Setup(m => m.FindByEmailAsync("resend-key@urbeat.local")).ReturnsAsync(user);
        userManagerMock.Setup(m => m.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        userManagerMock.Setup(m => m.GenerateEmailConfirmationTokenAsync(user)).ReturnsAsync("resend-token");
        userManagerMock.Setup(m => m.GetRolesAsync(user)).ReturnsAsync(new List<string> { "Customer" });

        var capturedKeys = new List<string>();
        var writer = new Mock<IOutboxWriter>();
        writer.Setup(x => x.EnqueueAsync(
                OutboxEventTypes.OutboundMessageRequested,
                It.IsAny<Guid>(),
                It.IsAny<OutboundMessageRequestedEvent>(),
                It.IsAny<DateTime>(),
                It.IsAny<long>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Guid, OutboundMessageRequestedEvent, DateTime, long, string?, CancellationToken>(
                (_, _, evt, _, _, _, _) => capturedKeys.Add(evt.DeliveryKey))
            .Returns(Task.CompletedTask);

        using var db = CreateDbContext();
        var sut = new EmailConfirmationService(
            userManagerMock.Object,
            Options.Create(DefaultOptions()),
            db,
            new Mock<IEmailTokenCache>().Object,
            writer.Object,
            NullLogger<EmailConfirmationService>.Instance);

        await sut.SendConfirmationEmailAsync(user.Id);
        await sut.ResendAsync(new ResendEmailConfirmationRequestDto { Email = user.Email });

        capturedKeys.Should().HaveCount(2);
        capturedKeys.Distinct().Should().HaveCount(2, "each confirmation request must have a unique delivery key so resend is not deduplicated");
        capturedKeys.Should().OnlyContain(x => x.StartsWith("email-confirm:", StringComparison.Ordinal));
    }
}
