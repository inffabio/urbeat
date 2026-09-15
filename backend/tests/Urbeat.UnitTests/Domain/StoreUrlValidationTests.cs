using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Application.Validators;

namespace Urbeat.UnitTests.Domain;

public sealed class StoreUrlValidationTests
{
    private const string RequiredMessage = "A URL da loja é obrigatória.";
    private const string MinLengthMessage = "A URL da loja deve ter pelo menos 3 caracteres.";
    private const string FormatMessage = "Use apenas letras minúsculas, números e hífens, sem hífens consecutivos ou nas extremidades.";

    public static TheoryData<string> EmptySlugs => new()
    {
        string.Empty,
        "   ",
    };

    public static TheoryData<string> InvalidFormatSlugs => new()
    {
        "-loja",
        "loja-",
        "loja--x",
        "Loja",
        "loja loja",
        "loja_1",
        "minha.loja",
        " minha-loja",
        "minha-loja ",
        " minha-loja ",
    };

    public static TheoryData<string> ValidSlugs => new()
    {
        "abc",
        "loja-teste",
        "loja123",
        "minha-loja-1",
    };

    [Theory]
    [MemberData(nameof(EmptySlugs))]
    public async Task CreateValidator_ShouldRejectEmptySlug(string slug)
    {
        var result = await new CreateStoreRequestDtoValidator().ValidateAsync(CreateRequestWith(slug));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(
            error => error.PropertyName == nameof(CreateStoreRequestDto.Slug) && error.ErrorMessage == RequiredMessage);
    }

    [Theory]
    [MemberData(nameof(EmptySlugs))]
    public async Task UpdateValidator_ShouldRejectEmptySlug(string slug)
    {
        var result = await new UpdateStoreRequestDtoValidator().ValidateAsync(UpdateRequestWith(slug));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(
            error => error.PropertyName == nameof(UpdateStoreRequestDto.Slug) && error.ErrorMessage == RequiredMessage);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("a")]
    public async Task CreateValidator_ShouldRejectShortSlug(string slug)
    {
        var result = await new CreateStoreRequestDtoValidator().ValidateAsync(CreateRequestWith(slug));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(
            error => error.PropertyName == nameof(CreateStoreRequestDto.Slug) && error.ErrorMessage == MinLengthMessage);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("a")]
    public async Task UpdateValidator_ShouldRejectShortSlug(string slug)
    {
        var result = await new UpdateStoreRequestDtoValidator().ValidateAsync(UpdateRequestWith(slug));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(
            error => error.PropertyName == nameof(UpdateStoreRequestDto.Slug) && error.ErrorMessage == MinLengthMessage);
    }

    [Theory]
    [MemberData(nameof(InvalidFormatSlugs))]
    public async Task CreateValidator_ShouldRejectInvalidSlugFormat(string slug)
    {
        var result = await new CreateStoreRequestDtoValidator().ValidateAsync(CreateRequestWith(slug));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(
            error => error.PropertyName == nameof(CreateStoreRequestDto.Slug) && error.ErrorMessage == FormatMessage);
    }

    [Theory]
    [MemberData(nameof(InvalidFormatSlugs))]
    public async Task UpdateValidator_ShouldRejectInvalidSlugFormat(string slug)
    {
        var result = await new UpdateStoreRequestDtoValidator().ValidateAsync(UpdateRequestWith(slug));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(
            error => error.PropertyName == nameof(UpdateStoreRequestDto.Slug) && error.ErrorMessage == FormatMessage);
    }

    [Theory]
    [MemberData(nameof(ValidSlugs))]
    public async Task CreateValidator_ShouldAcceptValidSlug(string slug)
    {
        var result = await new CreateStoreRequestDtoValidator().ValidateAsync(CreateRequestWith(slug));

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(ValidSlugs))]
    public async Task UpdateValidator_ShouldAcceptValidSlug(string slug)
    {
        var result = await new UpdateStoreRequestDtoValidator().ValidateAsync(UpdateRequestWith(slug));

        result.IsValid.Should().BeTrue();
    }

    private static CreateStoreRequestDto CreateRequestWith(string slug) => new()
    {
        Name = "Loja Teste",
        Slug = slug,
        PhoneNumber = "21999999999",
        CuisineType = "Lanches",
        SupportsDelivery = true,
        SupportsPickup = true,
        MaxDeliveryRadiusKm = 10,
    };

    private static UpdateStoreRequestDto UpdateRequestWith(string slug) => new()
    {
        Name = "Loja Teste",
        Slug = slug,
        PhoneNumber = "21999999999",
        CuisineType = "Lanches",
        SupportsDelivery = true,
        SupportsPickup = true,
        MaxDeliveryRadiusKm = 10,
    };
}
