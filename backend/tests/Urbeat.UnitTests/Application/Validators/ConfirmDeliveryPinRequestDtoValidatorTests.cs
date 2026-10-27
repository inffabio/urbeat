using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Application.Validators;

namespace Urbeat.UnitTests.Application.Validators;

public sealed class ConfirmDeliveryPinRequestDtoValidatorTests
{
    private readonly ConfirmDeliveryPinRequestDtoValidator _validator = new();

    [Theory]
    [InlineData("0000")]
    [InlineData("1234")]
    [InlineData("9999")]
    public async Task ValidateAsync_ShouldAcceptExactlyFourDigits(string pin)
    {
        var result = await _validator.ValidateAsync(new ConfirmDeliveryPinRequestDto { Pin = pin });

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("12345")]
    [InlineData("123456")]
    [InlineData("12a4")]
    [InlineData(" 1234")]
    public async Task ValidateAsync_ShouldRejectAnyOtherLengthOrNonDigits(string pin)
    {
        var result = await _validator.ValidateAsync(new ConfirmDeliveryPinRequestDto { Pin = pin });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.ErrorMessage.Contains("4 dígitos"));
    }
}
