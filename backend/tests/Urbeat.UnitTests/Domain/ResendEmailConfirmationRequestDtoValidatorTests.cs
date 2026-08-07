using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Application.Validators;

namespace Urbeat.UnitTests.Domain;

public sealed class ResendEmailConfirmationRequestDtoValidatorTests
{
    [Fact]
    public async Task ValidateAsync_ShouldReturnValid_WhenEmailIsValid()
    {
        var validator = new ResendEmailConfirmationRequestDtoValidator();
        var request = new ResendEmailConfirmationRequestDto
        {
            Email = "cliente@urbeat.local"
        };

        var result = await validator.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("@urbeat.local")]
    [InlineData("user@")]
    public async Task ValidateAsync_ShouldReturnInvalid_WhenEmailIsInvalid(string email)
    {
        var validator = new ResendEmailConfirmationRequestDtoValidator();
        var request = new ResendEmailConfirmationRequestDto
        {
            Email = email
        };

        var result = await validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ResendEmailConfirmationRequestDto.Email));
    }
}
