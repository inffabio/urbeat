using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Application.Validators;

namespace Urbeat.UnitTests.Domain;

public sealed class StoreNameValidationTests
{
    [Fact]
    public async Task CreateValidator_ShouldAcceptStoreNameWithExactlyOneHundredCharacters()
    {
        var result = await new CreateStoreRequestDtoValidator().ValidateAsync(CreateRequest(new string('a', 100)));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task CreateValidator_ShouldRejectStoreNameLongerThanOneHundredCharacters()
    {
        var result = await new CreateStoreRequestDtoValidator().ValidateAsync(CreateRequest(new string('a', 101)));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(CreateStoreRequestDto.Name));
    }

    [Fact]
    public async Task UpdateValidator_ShouldAcceptStoreNameWithExactlyOneHundredCharacters()
    {
        var result = await new UpdateStoreRequestDtoValidator().ValidateAsync(UpdateRequest(new string('a', 100)));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateValidator_ShouldRejectStoreNameLongerThanOneHundredCharacters()
    {
        var result = await new UpdateStoreRequestDtoValidator().ValidateAsync(UpdateRequest(new string('a', 101)));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(UpdateStoreRequestDto.Name));
    }

    private static CreateStoreRequestDto CreateRequest(string name) => new()
    {
        Name = name,
        Slug = "loja-teste",
        PhoneNumber = "21999999999",
        CuisineType = "Lanches",
        SupportsDelivery = true,
        SupportsPickup = true,
        MaxDeliveryRadiusKm = 10,
    };

    private static UpdateStoreRequestDto UpdateRequest(string name) => new()
    {
        Name = name,
        Slug = "loja-teste",
        PhoneNumber = "21999999999",
        CuisineType = "Lanches",
        SupportsDelivery = true,
        SupportsPickup = true,
        MaxDeliveryRadiusKm = 10,
    };
}
