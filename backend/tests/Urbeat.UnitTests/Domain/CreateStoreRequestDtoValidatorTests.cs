using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Application.Validators;

namespace Urbeat.UnitTests.Domain;

public sealed class CreateStoreRequestDtoValidatorTests
{
    private static CreateStoreRequestDto BuildRequest(string cuisineType) => new()
    {
        Name = "Loja Teste",
        Slug = "loja-teste",
        PhoneNumber = "21999999999",
        CuisineType = cuisineType,
        SupportsDelivery = true,
        SupportsPickup = true,
        InitialMinute = 30,
        FinalMinute = 45,
        MaxDeliveryRadiusKm = 10,
    };

    [Fact]
    public async Task ValidateAsync_ShouldRejectEmptyCuisineType()
    {
        var result = await new CreateStoreRequestDtoValidator().ValidateAsync(BuildRequest(string.Empty));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(CreateStoreRequestDto.CuisineType));
    }

    [Fact]
    public async Task ValidateAsync_ShouldRejectWhitespaceOnlyCuisineType()
    {
        var result = await new CreateStoreRequestDtoValidator().ValidateAsync(BuildRequest("   "));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(CreateStoreRequestDto.CuisineType));
    }

    [Fact]
    public async Task ValidateAsync_ShouldAcceptCuisineType()
    {
        var result = await new CreateStoreRequestDtoValidator().ValidateAsync(BuildRequest("Lanches"));

        result.IsValid.Should().BeTrue();
    }
}
