using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Application.Validators;

namespace Urbeat.UnitTests.Application.Validators;

public sealed class UpsertStoreDeliveryDriverRequestDtoValidatorTests
{
    private readonly UpsertStoreDeliveryDriverRequestDtoValidator _validator = new();

    [Theory]
    [InlineData("11999998888")]
    [InlineData("(11) 99999-8888")]
    public async Task ValidateAsync_ShouldAcceptMobileWithOrWithoutMask(string phone)
    {
        var result = await _validator.ValidateAsync(new UpsertStoreDeliveryDriverRequestDto
        {
            Name = "Motoboy Válido",
            PhoneNumber = phone,
            WhatsAppPhoneNumber = phone
        });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_ShouldKeepWhatsAppOptional()
    {
        var result = await _validator.ValidateAsync(new UpsertStoreDeliveryDriverRequestDto
        {
            Name = "Motoboy Sem WhatsApp",
            PhoneNumber = "(11) 99999-8888",
            WhatsAppPhoneNumber = null
        });

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("1199999888")]
    [InlineData("(11) 9999-8888")]
    [InlineData("(00) 99999-8888")]
    [InlineData("(11) 89999-8888")]
    [InlineData("(11) 99999-8888x")]
    public async Task ValidateAsync_ShouldRejectInvalidMainPhone(string phone)
    {
        var result = await _validator.ValidateAsync(new UpsertStoreDeliveryDriverRequestDto
        {
            Name = "Motoboy Inválido",
            PhoneNumber = phone
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.PropertyName == nameof(UpsertStoreDeliveryDriverRequestDto.PhoneNumber));
    }

    [Theory]
    [InlineData("1199999888")]
    [InlineData("(11) 89999-8888")]
    [InlineData("11999A98888")]
    public async Task ValidateAsync_ShouldRejectInvalidWhatsApp(string phone)
    {
        var result = await _validator.ValidateAsync(new UpsertStoreDeliveryDriverRequestDto
        {
            Name = "Motoboy Inválido",
            PhoneNumber = "(11) 99999-8888",
            WhatsAppPhoneNumber = phone
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.PropertyName == nameof(UpsertStoreDeliveryDriverRequestDto.WhatsAppPhoneNumber));
    }
}
