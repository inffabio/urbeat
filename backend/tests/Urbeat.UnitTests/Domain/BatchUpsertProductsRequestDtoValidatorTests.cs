using FluentAssertions;
using FluentValidation;
using Urbeat.Application.DTOs;
using Urbeat.Application.Validators;
using Urbeat.Domain.Enums;

namespace Urbeat.UnitTests.Domain;

public sealed class BatchUpsertProductsRequestDtoValidatorTests
{
    private readonly IValidator<BatchUpsertProductsRequestDto> _validator = new BatchUpsertProductsRequestDtoValidator();

    [Theory]
    [InlineData(ProductSpecialClassification.Offers)]
    [InlineData(ProductSpecialClassification.BestSeller)]
    [InlineData(ProductSpecialClassification.New)]
    public async Task ValidateAsync_ShouldAcceptEachApprovedSpecialClassification(ProductSpecialClassification classification)
    {
        var result = await _validator.ValidateAsync(RequestWith(classification));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_ShouldAcceptAnUnclassifiedBatchItem()
    {
        var result = await _validator.ValidateAsync(RequestWith(null));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_ShouldRejectANullItemsCollection()
    {
        var result = await _validator.ValidateAsync(new BatchUpsertProductsRequestDto { Items = null! });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(BatchUpsertProductsRequestDto.Items));
    }

    [Fact]
    public async Task ValidateAsync_ShouldStillAcceptAnEmptyBatchToClearTheCatalog()
    {
        var result = await _validator.ValidateAsync(new BatchUpsertProductsRequestDto { Items = [] });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_ShouldRejectUnknownSpecialClassification()
    {
        var result = await _validator.ValidateAsync(RequestWith((ProductSpecialClassification)99));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x =>
            x.PropertyName == $"{nameof(BatchUpsertProductsRequestDto.Items)}[0].{nameof(BatchProductItemDto.SpecialClassification)}");
    }

    private static BatchUpsertProductsRequestDto RequestWith(ProductSpecialClassification? classification)
        => new()
        {
            Items =
            [
                new BatchProductItemDto
                {
                    CategoryId = Guid.NewGuid(),
                    Name = "X-Bacon",
                    Description = "Bacon",
                    Price = 25.90m,
                    ImageUrl = "https://example.com/x-bacon.jpg",
                    IsAvailable = true,
                    DisplayOrder = 1,
                    SpecialClassification = classification,
                },
            ],
        };
}
