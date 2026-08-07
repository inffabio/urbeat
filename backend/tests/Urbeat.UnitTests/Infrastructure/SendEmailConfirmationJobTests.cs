using FluentAssertions;
using Urbeat.Application.Interfaces;
using Urbeat.Infrastructure.Jobs;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Urbeat.UnitTests.Infrastructure;

public sealed class SendEmailConfirmationJobTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldDelegateToEmailConfirmationService()
    {
        var userId = Guid.NewGuid();
        var emailConfirmationServiceMock = new Mock<IEmailConfirmationService>();

        var sut = new SendEmailConfirmationJob(
            emailConfirmationServiceMock.Object,
            NullLogger<SendEmailConfirmationJob>.Instance);

        await sut.ExecuteAsync(userId);

        emailConfirmationServiceMock.Verify(
            s => s.SendConfirmationEmailAsync(userId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldPropagateExceptionFromService()
    {
        var userId = Guid.NewGuid();
        var emailConfirmationServiceMock = new Mock<IEmailConfirmationService>();
        emailConfirmationServiceMock
            .Setup(s => s.SendConfirmationEmailAsync(userId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var sut = new SendEmailConfirmationJob(
            emailConfirmationServiceMock.Object,
            NullLogger<SendEmailConfirmationJob>.Instance);

        var act = async () => await sut.ExecuteAsync(userId);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }
}
