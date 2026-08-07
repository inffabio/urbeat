using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Services.Email;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.UnitTests.Infrastructure;

public class EmailConfirmationServiceShortCodeTests
{
    private readonly Mock<UserManager<IdentityUser<Guid>>> _userManagerMock;
    private readonly Mock<IEmailService> _emailServiceMock;
    private readonly Mock<IEmailTokenCache> _emailTokenCacheMock;
    private readonly Mock<ILogger<EmailConfirmationService>> _loggerMock;
    private readonly IOptions<EmailConfirmationOptions> _options;

    public EmailConfirmationServiceShortCodeTests()
    {
        var store = new Mock<IUserStore<IdentityUser<Guid>>>();
        _userManagerMock = new Mock<UserManager<IdentityUser<Guid>>>(
            store.Object,
            Options.Create(new IdentityOptions()),
            new Mock<IPasswordHasher<IdentityUser<Guid>>>().Object,
            Array.Empty<IUserValidator<IdentityUser<Guid>>>(),
            Array.Empty<IPasswordValidator<IdentityUser<Guid>>>(),
            new Mock<ILookupNormalizer>().Object,
            new IdentityErrorDescriber(),
            new Mock<IServiceProvider>().Object,
            new Mock<ILogger<UserManager<IdentityUser<Guid>>>>().Object);
        _emailServiceMock = new Mock<IEmailService>();
        _emailTokenCacheMock = new Mock<IEmailTokenCache>();
        _loggerMock = new Mock<ILogger<EmailConfirmationService>>();
        
        var opts = new EmailConfirmationOptions { FrontendBaseUrl = "http://localhost", ConfirmPath = "confirmar-email" };
        _options = Options.Create(opts);
    }

    [Fact]
    public async Task ConfirmByShortCodeAsync_InvalidToken_ReturnsError()
    {
        // Arrange
        _emailTokenCacheMock.Setup(c => c.GetMappingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmailTokenData?)null);

        var service = new EmailConfirmationService(_userManagerMock.Object, _emailServiceMock.Object, _options, new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options), _emailTokenCacheMock.Object, _loggerMock.Object);

        // Act
        var result = await service.ConfirmByShortCodeAsync("invalidCode");

        // Assert
        result.InvalidToken.Should().BeTrue();
        result.Errors.Should().Contain("Link de confirmação inválido ou expirado.");
    }

    [Fact]
    public async Task ConfirmByShortCodeAsync_ValidToken_CallsConfirmAsync()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var mapping = new EmailTokenData { UserId = userId, Token = "token_base64" };
        _emailTokenCacheMock.Setup(c => c.GetMappingAsync("validCode", It.IsAny<CancellationToken>()))
            .ReturnsAsync(mapping);

        var user = new IdentityUser<Guid> { Id = userId };
        _userManagerMock.Setup(u => u.FindByIdAsync(userId.ToString())).ReturnsAsync(user);
        
        // Mock identity confirmation
        _userManagerMock.Setup(u => u.ConfirmEmailAsync(user, It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);

        var service = new EmailConfirmationService(_userManagerMock.Object, _emailServiceMock.Object, _options, new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options), _emailTokenCacheMock.Object, _loggerMock.Object);

        // Act
        var result = await service.ConfirmByShortCodeAsync("validCode");

        // Assert
        result.Succeeded.Should().BeTrue();
    }
}
