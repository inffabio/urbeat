using FluentAssertions;
using FluentValidation;
using Urbeat.Application.DTOs;
using Urbeat.Application.Validators;

namespace Urbeat.UnitTests.Domain;

public sealed class CreateProductRequestDtoValidatorTests
{
    private readonly IValidator<CreateProductRequestDto> _validator = new CreateProductRequestDtoValidator();

    [Fact]
    public async Task ValidateAsync_ShouldReturnValidResult_WhenRequestIsValid()
    {
        var request = new CreateProductRequestDto
        {
            CategoryId = Guid.NewGuid(),
            Name = "Pizza Margherita",
            Description = "Pizza classica",
            Price = 45.90m,
            ImageUrl = "https://example.com/image.jpg",
            DisplayOrder = 1
        };

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_ShouldReturnInvalidResult_WhenImageUrlIsNull()
    {
        var request = new CreateProductRequestDto
        {
            CategoryId = Guid.NewGuid(),
            Name = "Coca-Cola",
            Price = 8.50m,
            DisplayOrder = 0
        };

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(CreateProductRequestDto.ImageUrl));
    }

    [Fact]
    public async Task ValidateAsync_ShouldReturnInvalidResult_WhenImageUrlIsEmpty()
    {
        var request = new CreateProductRequestDto
        {
            CategoryId = Guid.NewGuid(),
            Name = "Coca-Cola",
            Price = 8.50m,
            ImageUrl = string.Empty,
            DisplayOrder = 0
        };

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(CreateProductRequestDto.ImageUrl));
    }

    [Fact]
    public async Task ValidateAsync_ShouldReturnInvalidResult_WhenCategoryIdIsEmpty()
    {
        var request = new CreateProductRequestDto
        {
            CategoryId = Guid.Empty,
            Name = "Pizza",
            Price = 30m,
            DisplayOrder = 0
        };

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(CreateProductRequestDto.CategoryId));
    }

    [Fact]
    public async Task ValidateAsync_ShouldReturnInvalidResult_WhenNameIsEmpty()
    {
        var request = new CreateProductRequestDto
        {
            CategoryId = Guid.NewGuid(),
            Name = string.Empty,
            Price = 30m,
            DisplayOrder = 0
        };

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(CreateProductRequestDto.Name));
    }

    [Fact]
    public async Task ValidateAsync_ShouldReturnInvalidResult_WhenNameExceedsMaxLength()
    {
        var request = new CreateProductRequestDto
        {
            CategoryId = Guid.NewGuid(),
            Name = new string('A', 121),
            Price = 30m,
            DisplayOrder = 0
        };

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(CreateProductRequestDto.Name));
    }

    [Fact]
    public async Task ValidateAsync_ShouldReturnInvalidResult_WhenPriceIsZero()
    {
        var request = new CreateProductRequestDto
        {
            CategoryId = Guid.NewGuid(),
            Name = "Pizza",
            Price = 0m,
            DisplayOrder = 0
        };

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(CreateProductRequestDto.Price));
    }

    [Fact]
    public async Task ValidateAsync_ShouldReturnInvalidResult_WhenPriceIsNegative()
    {
        var request = new CreateProductRequestDto
        {
            CategoryId = Guid.NewGuid(),
            Name = "Pizza",
            Price = -10m,
            DisplayOrder = 0
        };

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(CreateProductRequestDto.Price));
    }

    [Fact]
    public async Task ValidateAsync_ShouldReturnInvalidResult_WhenDisplayOrderIsNegative()
    {
        var request = new CreateProductRequestDto
        {
            CategoryId = Guid.NewGuid(),
            Name = "Pizza",
            Price = 30m,
            DisplayOrder = -1
        };

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(CreateProductRequestDto.DisplayOrder));
    }

    [Fact]
    public async Task ValidateAsync_ShouldReturnInvalidResult_WhenDescriptionExceedsMaxLength()
    {
        var request = new CreateProductRequestDto
        {
            CategoryId = Guid.NewGuid(),
            Name = "Pizza",
            Description = new string('A', 501),
            Price = 30m,
            DisplayOrder = 0
        };

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(CreateProductRequestDto.Description));
    }
}
