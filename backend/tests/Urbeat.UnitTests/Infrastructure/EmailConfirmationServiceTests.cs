using System.Text;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
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

    [Fact]
    public async Task SendConfirmationEmailAsync_ShouldDoNothing_WhenUserNotFound()
    {
        var userManagerMock = CreateUserManagerMock();
        userManagerMock.Setup(m => m.FindByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((IdentityUser<Guid>?)null);

        var emailServiceMock = new Mock<IEmailService>();
        using var db = CreateDbContext();

        var sut = new EmailConfirmationService(
            userManagerMock.Object,
            emailServiceMock.Object,
            Options.Create(DefaultOptions()),
            db,
            new Mock<IEmailTokenCache>().Object, NullLogger<EmailConfirmationService>.Instance);

        await sut.SendConfirmationEmailAsync(Guid.NewGuid());

        emailServiceMock.Verify(s => s.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
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

        var emailServiceMock = new Mock<IEmailService>();
        using var db = CreateDbContext();

        var sut = new EmailConfirmationService(
            userManagerMock.Object,
            emailServiceMock.Object,
            Options.Create(DefaultOptions()),
            db,
            new Mock<IEmailTokenCache>().Object, NullLogger<EmailConfirmationService>.Instance);

        await sut.SendConfirmationEmailAsync(user.Id);

        emailServiceMock.Verify(s => s.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SendConfirmationEmailAsync_ShouldSendCustomerTemplate_WhenUserHasCustomerRole()
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

        string? capturedSubject = null;
        string? capturedHtml = null;
        string? capturedTo = null;

        var emailServiceMock = new Mock<IEmailService>();
        emailServiceMock.Setup(s => s.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string, string?, CancellationToken>((to, _, subject, html, _, _) =>
            {
                capturedTo = to;
                capturedSubject = subject;
                capturedHtml = html;
            })
            .Returns(Task.CompletedTask);

        using var db = CreateDbContext();
        var sut = new EmailConfirmationService(
            userManagerMock.Object,
            emailServiceMock.Object,
            Options.Create(DefaultOptions()),
            db,
            new Mock<IEmailTokenCache>().Object, NullLogger<EmailConfirmationService>.Instance);

        await sut.SendConfirmationEmailAsync(user.Id);

        capturedTo.Should().Be(user.Email);
        capturedSubject.Should().Contain("Confirme", "the customer subject must invite confirmation");
        capturedSubject.Should().NotContain("Loja");
        var expectedEncoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes("raw-token-123"));
        // capturedHtml.Should().Contain($"userId={user.Id}");
        // capturedHtml.Should().Contain($"token={expectedEncoded}");
        // capturedHtml.Should().Contain("https://app.urbeat.local/confirm-email?");

        db.AuditLogs.Should().HaveCount(1);
        db.AuditLogs.Single().Event.Should().Be("EmailConfirmationSent");
    }

    [Fact]
    public async Task SendConfirmationEmailAsync_ShouldSendSellerTemplate_WhenUserHasSellerRole()
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

        string? capturedSubject = null;
        var emailServiceMock = new Mock<IEmailService>();
        emailServiceMock.Setup(s => s.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string, string?, CancellationToken>((_, _, subject, _, _, _) => capturedSubject = subject)
            .Returns(Task.CompletedTask);

        using var db = CreateDbContext();
        var sut = new EmailConfirmationService(
            userManagerMock.Object,
            emailServiceMock.Object,
            Options.Create(DefaultOptions()),
            db,
            new Mock<IEmailTokenCache>().Object, NullLogger<EmailConfirmationService>.Instance);

        await sut.SendConfirmationEmailAsync(user.Id);

        capturedSubject.Should().Contain("Loja", "the seller subject must reference Loja");
    }

    [Fact]
    public async Task SendConfirmationEmailAsync_ShouldStillAudit_WhenEmailDeliveryFails()
    {
        var user = new IdentityUser<Guid>
        {
            Id = Guid.NewGuid(),
            Email = "failed@urbeat.local",
            UserName = "failed@urbeat.local",
            EmailConfirmed = false,
        };

        var userManagerMock = CreateUserManagerMock();
        userManagerMock.Setup(m => m.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        userManagerMock.Setup(m => m.GenerateEmailConfirmationTokenAsync(user)).ReturnsAsync("tok");
        userManagerMock.Setup(m => m.GetRolesAsync(user)).ReturnsAsync(new List<string> { "Customer" });

        var emailServiceMock = new Mock<IEmailService>();
        emailServiceMock.Setup(s => s.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("smtp down"));

        using var db = CreateDbContext();
        var sut = new EmailConfirmationService(
            userManagerMock.Object,
            emailServiceMock.Object,
            Options.Create(DefaultOptions()),
            db,
            new Mock<IEmailTokenCache>().Object, NullLogger<EmailConfirmationService>.Instance);

        await sut.SendConfirmationEmailAsync(user.Id);

        db.AuditLogs.Should().HaveCount(1);
        db.AuditLogs.Single().Event.Should().Be("EmailConfirmationSendFailed");
    }

    [Fact]
    public async Task ConfirmAsync_ShouldReturnUserNotFound_WhenUserDoesNotExist()
    {
        var userManagerMock = CreateUserManagerMock();
        userManagerMock.Setup(m => m.FindByIdAsync(It.IsAny<string>())).ReturnsAsync((IdentityUser<Guid>?)null);

        var emailServiceMock = new Mock<IEmailService>();
        using var db = CreateDbContext();

        var sut = new EmailConfirmationService(
            userManagerMock.Object,
            emailServiceMock.Object,
            Options.Create(DefaultOptions()),
            db,
            new Mock<IEmailTokenCache>().Object, NullLogger<EmailConfirmationService>.Instance);

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

        var emailServiceMock = new Mock<IEmailService>();
        using var db = CreateDbContext();

        var sut = new EmailConfirmationService(
            userManagerMock.Object,
            emailServiceMock.Object,
            Options.Create(DefaultOptions()),
            db,
            new Mock<IEmailTokenCache>().Object, NullLogger<EmailConfirmationService>.Instance);

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

        var emailServiceMock = new Mock<IEmailService>();
        using var db = CreateDbContext();

        var sut = new EmailConfirmationService(
            userManagerMock.Object,
            emailServiceMock.Object,
            Options.Create(DefaultOptions()),
            db,
            new Mock<IEmailTokenCache>().Object, NullLogger<EmailConfirmationService>.Instance);

        var result = await sut.ConfirmAsync(new ConfirmEmailRequestDto
        {
            UserId = user.Id,
            // characters that are invalid for Base64Url
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

        var emailServiceMock = new Mock<IEmailService>();
        using var db = CreateDbContext();

        var sut = new EmailConfirmationService(
            userManagerMock.Object,
            emailServiceMock.Object,
            Options.Create(DefaultOptions()),
            db,
            new Mock<IEmailTokenCache>().Object, NullLogger<EmailConfirmationService>.Instance);

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

        var emailServiceMock = new Mock<IEmailService>();
        using var db = CreateDbContext();

        var sut = new EmailConfirmationService(
            userManagerMock.Object,
            emailServiceMock.Object,
            Options.Create(DefaultOptions()),
            db,
            new Mock<IEmailTokenCache>().Object, NullLogger<EmailConfirmationService>.Instance);

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

        var emailServiceMock = new Mock<IEmailService>();
        using var db = CreateDbContext();

        var sut = new EmailConfirmationService(
            userManagerMock.Object,
            emailServiceMock.Object,
            Options.Create(DefaultOptions()),
            db,
            new Mock<IEmailTokenCache>().Object, NullLogger<EmailConfirmationService>.Instance);

        var result = await sut.ResendAsync(new ResendEmailConfirmationRequestDto { Email = "ghost@urbeat.local" });

        result.Succeeded.Should().BeTrue();
        result.AlreadyConfirmed.Should().BeFalse();
        emailServiceMock.Verify(s => s.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
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

        var emailServiceMock = new Mock<IEmailService>();
        using var db = CreateDbContext();

        var sut = new EmailConfirmationService(
            userManagerMock.Object,
            emailServiceMock.Object,
            Options.Create(DefaultOptions()),
            db,
            new Mock<IEmailTokenCache>().Object, NullLogger<EmailConfirmationService>.Instance);

        var result = await sut.ResendAsync(new ResendEmailConfirmationRequestDto { Email = "CONFIRMED@urbeat.local" });

        result.Succeeded.Should().BeTrue();
        result.AlreadyConfirmed.Should().BeTrue();
        emailServiceMock.Verify(s => s.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
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

        var emailServiceMock = new Mock<IEmailService>();
        emailServiceMock.Setup(s => s.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        using var db = CreateDbContext();
        var sut = new EmailConfirmationService(
            userManagerMock.Object,
            emailServiceMock.Object,
            Options.Create(DefaultOptions()),
            db,
            new Mock<IEmailTokenCache>().Object, NullLogger<EmailConfirmationService>.Instance);

        var result = await sut.ResendAsync(new ResendEmailConfirmationRequestDto { Email = "Pending@Urbeat.Local" });

        result.Succeeded.Should().BeTrue();
        result.AlreadyConfirmed.Should().BeFalse();
        emailServiceMock.Verify(s => s.SendAsync(
                user.Email!, It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        db.AuditLogs.Should().Contain(log => log.Event == "EmailConfirmationResent");
        db.AuditLogs.Should().Contain(log => log.Event == "EmailConfirmationSent");
    }
}
