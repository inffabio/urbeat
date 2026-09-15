using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Application.Validators;

namespace Urbeat.UnitTests.Domain;

public sealed class CreateCuisineTypeRequestDtoValidatorTests
{
    [Fact]
    public async Task ValidateAsync_ShouldRejectEmptyName()
    {
        var result = await new CreateCuisineTypeRequestDtoValidator()
            .ValidateAsync(new CreateCuisineTypeRequestDto { Name = string.Empty });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(CreateCuisineTypeRequestDto.Name));
    }

    [Fact]
    public async Task ValidateAsync_ShouldRejectWhitespaceOnlyName()
    {
        var result = await new CreateCuisineTypeRequestDtoValidator()
            .ValidateAsync(new CreateCuisineTypeRequestDto { Name = "   " });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(CreateCuisineTypeRequestDto.Name));
    }

    [Fact]
    public async Task ValidateAsync_ShouldRejectNameLongerThanEightyCharacters()
    {
        var result = await new CreateCuisineTypeRequestDtoValidator()
            .ValidateAsync(new CreateCuisineTypeRequestDto { Name = new string('a', 81) });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(CreateCuisineTypeRequestDto.Name));
    }

    [Fact]
    public async Task ValidateAsync_ShouldAcceptValidName()
    {
        var result = await new CreateCuisineTypeRequestDtoValidator()
            .ValidateAsync(new CreateCuisineTypeRequestDto { Name = "Comida Baiana" });

        result.IsValid.Should().BeTrue();
    }
}
