using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Application.Validators;

namespace Urbeat.UnitTests.Application.Validators;

public sealed class CancelOrderRequestDtoValidatorTests
{
    private readonly CancelOrderRequestDtoValidator _validator = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    [InlineData("abcd")]
    public void ShouldReject_ReasonShorterThanFiveCharacters(string? reason)
    {
        var result = _validator.Validate(new CancelOrderRequestDto { Reason = reason });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(CancelOrderRequestDto.Reason));
    }

    [Theory]
    [InlineData("abcde")]
    [InlineData("  motivo do cancelamento  ")]
    public void ShouldAccept_ReasonWithAtLeastFiveCharacters(string reason)
    {
        var result = _validator.Validate(new CancelOrderRequestDto { Reason = reason });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ShouldReject_ReasonLongerThanTheColumn()
    {
        var result = _validator.Validate(new CancelOrderRequestDto
        {
            Reason = new string('a', CancelOrderRequestDto.MaximumReasonLength + 1)
        });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ShouldAccept_ReasonAtTheColumnLimitAfterTrimming()
    {
        var result = _validator.Validate(new CancelOrderRequestDto
        {
            Reason = $"  {new string('a', CancelOrderRequestDto.MaximumReasonLength)}  "
        });

        result.IsValid.Should().BeTrue();
    }
}
