using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Application.Validators;

namespace Urbeat.UnitTests.Application.Validators;

public sealed class UpdateSellerProfileRequestDtoValidatorTests
{
    [Fact]
    public async Task ValidateAsync_ShouldAcceptTrimmedNameBetweenThreeAndOneHundredTwentyCharacters()
    {
        var result = await new UpdateSellerProfileRequestDtoValidator()
            .ValidateAsync(new UpdateSellerProfileRequestDto { FullName = "  Contratante Valido  " });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_ShouldAcceptExactlyOneHundredTwentyCharacters()
    {
        var result = await new UpdateSellerProfileRequestDtoValidator()
            .ValidateAsync(new UpdateSellerProfileRequestDto { FullName = new string('a', 120) });

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateAsync_ShouldRejectEmptyOrWhitespaceName(string name)
    {
        var result = await new UpdateSellerProfileRequestDtoValidator()
            .ValidateAsync(new UpdateSellerProfileRequestDto { FullName = name });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(UpdateSellerProfileRequestDto.FullName));
    }

    [Fact]
    public async Task ValidateAsync_ShouldRejectNameShorterThanThreeTrimmedCharacters()
    {
        var result = await new UpdateSellerProfileRequestDtoValidator()
            .ValidateAsync(new UpdateSellerProfileRequestDto { FullName = "  ab  " });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(UpdateSellerProfileRequestDto.FullName));
    }

    [Fact]
    public async Task ValidateAsync_ShouldRejectNameLongerThanOneHundredTwentyCharacters()
    {
        var result = await new UpdateSellerProfileRequestDtoValidator()
            .ValidateAsync(new UpdateSellerProfileRequestDto { FullName = new string('a', 121) });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(UpdateSellerProfileRequestDto.FullName));
    }
}
