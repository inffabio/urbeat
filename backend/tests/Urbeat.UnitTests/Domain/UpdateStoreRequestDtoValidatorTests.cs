using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Application.Validators;

namespace Urbeat.UnitTests.Domain;

public sealed class UpdateStoreRequestDtoValidatorTests
{
    private static UpdateStoreRequestDto BuildRequest(string? document = null, string? pixKey = null) => new()
    {
        Name = "Loja Teste",
        Slug = "loja-teste",
        PhoneNumber = "21999999999",
        Document = document,
        PixKey = pixKey,
        CuisineType = "Lanches",
        Description = "Descrição da loja",
        SupportsDelivery = true,
        SupportsPickup = true,
        InitialMinute = 30,
        FinalMinute = 45,
        MaxDeliveryRadiusKm = 10,
    };

    [Fact]
    public async Task ValidateAsync_ShouldAcceptValidCpfAndOptionalPixKey()
    {
        var result = await new UpdateStoreRequestDtoValidator().ValidateAsync(BuildRequest("529.982.247-25", "pix@example.com"));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_ShouldRejectInvalidCpfOrCnpj()
    {
        var result = await new UpdateStoreRequestDtoValidator().ValidateAsync(BuildRequest("111.111.111-11"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(UpdateStoreRequestDto.Document));
    }

    [Fact]
    public async Task ValidateAsync_ShouldRejectPixKeyLongerThanFiftyCharacters()
    {
        var result = await new UpdateStoreRequestDtoValidator().ValidateAsync(BuildRequest(pixKey: new string('a', 51)));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(UpdateStoreRequestDto.PixKey));
    }
}
