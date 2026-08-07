using CommonTestUtilities.Fixtures;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Application.Validators;

namespace Urbeat.UnitTests.Domain;

public sealed class ConfirmEmailRequestDtoValidatorTests
{
    [Fact]
    public async Task ValidateAsync_ShouldReturnValid_WhenAllFieldsAreProvided()
    {
        var validator = new ConfirmEmailRequestDtoValidator();
        var request = EmailConfirmationDataFixture.BuildValidConfirmRequest();

        var result = await validator.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_ShouldFail_WhenUserIdIsEmpty()
    {
        var validator = new ConfirmEmailRequestDtoValidator();
        var request = new ConfirmEmailRequestDto
        {
            UserId = Guid.Empty,
            Token = "some-token"
        };

        var result = await validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ConfirmEmailRequestDto.UserId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateAsync_ShouldFail_WhenTokenIsNullOrWhitespace(string token)
    {
        var validator = new ConfirmEmailRequestDtoValidator();
        var request = new ConfirmEmailRequestDto
        {
            UserId = Guid.NewGuid(),
            Token = token
        };

        var result = await validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ConfirmEmailRequestDto.Token));
    }

    [Fact]
    public async Task ValidateAsync_ShouldFail_WhenTokenExceedsMaximumLength()
    {
        var validator = new ConfirmEmailRequestDtoValidator();
        var request = new ConfirmEmailRequestDto
        {
            UserId = Guid.NewGuid(),
            Token = new string('a', 2049)
        };

        var result = await validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ConfirmEmailRequestDto.Token));
    }
}
