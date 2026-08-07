using FluentAssertions;
using FluentValidation;
using Urbeat.Application.DTOs;
using Urbeat.Application.Validators;

namespace Urbeat.UnitTests.Domain;

public sealed class UpdateProductRequestDtoValidatorTests
{
    private readonly IValidator<UpdateProductRequestDto> _validator = new UpdateProductRequestDtoValidator();

    [Fact]
    public async Task ValidateAsync_ShouldReturnValidResult_WhenRequestIsValid()
    {
        var request = new UpdateProductRequestDto
        {
            CategoryId = Guid.NewGuid(),
            Name = "Pizza Margherita",
            Description = "Pizza classica",
            Price = 49.90m,
            ImageUrl = "https://example.com/image.jpg",
            IsAvailable = true,
            DisplayOrder = 1
        };

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_ShouldReturnInvalidResult_WhenImageUrlIsNull()
    {
        var request = new UpdateProductRequestDto
        {
            CategoryId = Guid.NewGuid(),
            Name = "Coca-Cola",
            Price = 8.50m,
            DisplayOrder = 0
        };

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(UpdateProductRequestDto.ImageUrl));
    }

    [Fact]
    public async Task ValidateAsync_ShouldReturnInvalidResult_WhenCategoryIdIsEmpty()
    {
        var request = new UpdateProductRequestDto
        {
            CategoryId = Guid.Empty,
            Name = "Pizza",
            Price = 30m,
            DisplayOrder = 0
        };

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(UpdateProductRequestDto.CategoryId));
    }

    [Fact]
    public async Task ValidateAsync_ShouldReturnInvalidResult_WhenNameIsEmpty()
    {
        var request = new UpdateProductRequestDto
        {
            CategoryId = Guid.NewGuid(),
            Name = string.Empty,
            Price = 30m,
            DisplayOrder = 0
        };

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(UpdateProductRequestDto.Name));
    }

    [Fact]
    public async Task ValidateAsync_ShouldReturnInvalidResult_WhenNameExceedsMaxLength()
    {
        var request = new UpdateProductRequestDto
        {
            CategoryId = Guid.NewGuid(),
            Name = new string('A', 121),
            Price = 30m,
            DisplayOrder = 0
        };

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(UpdateProductRequestDto.Name));
    }

    [Fact]
    public async Task ValidateAsync_ShouldReturnInvalidResult_WhenPriceIsZero()
    {
        var request = new UpdateProductRequestDto
        {
            CategoryId = Guid.NewGuid(),
            Name = "Pizza",
            Price = 0m,
            DisplayOrder = 0
        };

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(UpdateProductRequestDto.Price));
    }

    [Fact]
    public async Task ValidateAsync_ShouldReturnInvalidResult_WhenDisplayOrderIsNegative()
    {
        var request = new UpdateProductRequestDto
        {
            CategoryId = Guid.NewGuid(),
            Name = "Pizza",
            Price = 30m,
            DisplayOrder = -1
        };

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(UpdateProductRequestDto.DisplayOrder));
    }
}
