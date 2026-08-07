using FluentAssertions;
using FluentValidation;
using Urbeat.Application.DTOs;
using Urbeat.Application.Validators;

namespace Urbeat.UnitTests.Domain;

public sealed class CreateProductCategoryRequestDtoValidatorTests
{
    private readonly IValidator<CreateProductCategoryRequestDto> _validator = new CreateProductCategoryRequestDtoValidator();

    [Fact]
    public async Task ValidateAsync_ShouldReturnValidResult_WhenRequestIsValid()
    {
        var request = new CreateProductCategoryRequestDto
        {
            Name = "Bebidas",
            DisplayOrder = 1
        };

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_ShouldReturnInvalidResult_WhenNameIsEmpty()
    {
        var request = new CreateProductCategoryRequestDto
        {
            Name = string.Empty,
            DisplayOrder = 0
        };

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(CreateProductCategoryRequestDto.Name));
    }

    [Fact]
    public async Task ValidateAsync_ShouldReturnInvalidResult_WhenNameExceedsMaxLength()
    {
        var request = new CreateProductCategoryRequestDto
        {
            Name = new string('A', 81),
            DisplayOrder = 0
        };

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(CreateProductCategoryRequestDto.Name));
    }

    [Fact]
    public async Task ValidateAsync_ShouldReturnInvalidResult_WhenDisplayOrderIsNegative()
    {
        var request = new CreateProductCategoryRequestDto
        {
            Name = "Bebidas",
            DisplayOrder = -1
        };

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(CreateProductCategoryRequestDto.DisplayOrder));
    }
}
