using FluentAssertions;
using FluentValidation;
using Urbeat.Application.DTOs;
using Urbeat.Application.Validators;

namespace Urbeat.UnitTests.Domain;

public sealed class UpdateProductCategoryRequestDtoValidatorTests
{
    private readonly IValidator<UpdateProductCategoryRequestDto> _validator = new UpdateProductCategoryRequestDtoValidator();

    [Fact]
    public async Task ValidateAsync_ShouldReturnValidResult_WhenRequestIsValid()
    {
        var request = new UpdateProductCategoryRequestDto
        {
            Name = "Bebidas",
            DisplayOrder = 2,
            IsActive = true
        };

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_ShouldReturnInvalidResult_WhenNameIsEmpty()
    {
        var request = new UpdateProductCategoryRequestDto
        {
            Name = string.Empty,
            DisplayOrder = 0
        };

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(UpdateProductCategoryRequestDto.Name));
    }

    [Fact]
    public async Task ValidateAsync_ShouldReturnInvalidResult_WhenNameExceedsMaxLength()
    {
        var request = new UpdateProductCategoryRequestDto
        {
            Name = new string('A', 81),
            DisplayOrder = 0
        };

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(UpdateProductCategoryRequestDto.Name));
    }

    [Fact]
    public async Task ValidateAsync_ShouldReturnInvalidResult_WhenDisplayOrderIsNegative()
    {
        var request = new UpdateProductCategoryRequestDto
        {
            Name = "Bebidas",
            DisplayOrder = -1
        };

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(UpdateProductCategoryRequestDto.DisplayOrder));
    }
}
