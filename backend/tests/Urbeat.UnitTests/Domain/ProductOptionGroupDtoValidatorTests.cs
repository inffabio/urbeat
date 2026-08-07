using FluentAssertions;
using FluentValidation;
using Urbeat.Application.DTOs;
using Urbeat.Application.Validators;

namespace Urbeat.UnitTests.Domain;

public sealed class ProductOptionGroupDtoValidatorTests
{
    private readonly IValidator<ProductOptionGroupDto> _validator = new ProductOptionGroupDtoValidator();

    private static ProductOptionGroupDto Group(
        string name = "Adicionais",
        int min = 0,
        int max = 5,
        ProductOptionItemDto[]? items = null) => new()
    {
        Name = name,
        ChoiceType = "multiple",
        MinChoices = min,
        MaxChoices = max,
        Items = items ?? new[] { new ProductOptionItemDto { Name = "Bacon", Price = 4.50m, DisplayOrder = 0 } },
    };

    [Fact]
    public async Task Should_BeValid_WhenGroupIsWellFormed()
    {
        var result = await _validator.ValidateAsync(Group());
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Should_BeInvalid_WhenNameIsEmpty()
    {
        var result = await _validator.ValidateAsync(Group(name: string.Empty));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(ProductOptionGroupDto.Name));
    }

    [Fact]
    public async Task Should_BeInvalid_WhenMaxIsZero()
    {
        var result = await _validator.ValidateAsync(Group(min: 0, max: 0));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(ProductOptionGroupDto.MaxChoices));
    }

    [Fact]
    public async Task Should_BeInvalid_WhenMinGreaterThanMax()
    {
        var result = await _validator.ValidateAsync(Group(min: 4, max: 2));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Should_BeInvalid_WhenItemNameIsEmpty()
    {
        var result = await _validator.ValidateAsync(
            Group(items: new[] { new ProductOptionItemDto { Name = "", Price = 1m } }));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Should_BeInvalid_WhenItemPriceIsNegative()
    {
        var result = await _validator.ValidateAsync(
            Group(items: new[] { new ProductOptionItemDto { Name = "Bacon", Price = -1m } }));
        result.IsValid.Should().BeFalse();
    }
}
