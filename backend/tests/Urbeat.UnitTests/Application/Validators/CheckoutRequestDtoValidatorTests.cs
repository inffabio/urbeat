using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Application.Validators;
using Urbeat.Domain.Entities;

namespace Urbeat.UnitTests.Application.Validators;

public sealed class CheckoutRequestDtoValidatorTests
{
    private const string PaymentMethodNotAllowedMessage =
        "Esta forma de pagamento não está mais disponível. Atualize o aplicativo ou recarregue a página e escolha Pix na maquininha, dinheiro, débito ou crédito.";

    private readonly CheckoutRequestDtoValidator _validator = new();

    [Fact]
    public async Task ValidateAsync_ShouldAcceptLegacyCashPayloadWithoutTheNewContractFields()
    {
        // The cash-selection requirement belongs to the checkout service policy (it needs the
        // authoritative total). The request validator must not reject the legacy shape outright.
        var legacyCash = BuildRequest(PaymentMethod.CashOnDelivery);

        (await _validator.ValidateAsync(legacyCash)).IsValid
            .Should().BeTrue("cached storefronts that predate the cash-change contract must reach the service");
    }

    [Fact]
    public async Task ValidateAsync_ShouldRejectOnlinePixWithAnActionablePwaMessage()
    {
        var result = await _validator.ValidateAsync(BuildRequest(PaymentMethod.PixOnline));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(x => x.ErrorMessage == PaymentMethodNotAllowedMessage);
        result.Errors.Should().Contain(x => x.ErrorMessage.Contains("Atualize o aplicativo"),
            "the message must tell cached PWA/app clients how to recover");
    }

    [Theory]
    [InlineData(PaymentMethod.PixOnline)]
    [InlineData(PaymentMethod.CardOnline)]
    [InlineData(PaymentMethod.CardOnDelivery)]
    public async Task ValidateAsync_ShouldNeverReopenLegacyMethodsForNewOrders(PaymentMethod method)
    {
        var result = await _validator.ValidateAsync(BuildRequest(method));

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(PaymentMethod.PixOnDelivery)]
    [InlineData(PaymentMethod.DebitOnDelivery)]
    [InlineData(PaymentMethod.CreditOnDelivery)]
    public async Task ValidateAsync_ShouldAcceptTheCurrentStorefrontMethods(PaymentMethod method)
    {
        var result = await _validator.ValidateAsync(BuildRequest(method));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_ShouldReportCashPrecisionWithCleanPortuguese()
    {
        var request = BuildCashRequest(withoutChange: false, changeFor: 50.001m);

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.ErrorMessage == "O valor para troco deve ter no máximo duas casas decimais.");
    }

    [Fact]
    public async Task ValidateAsync_ShouldNeverReturnCorruptedCharacters()
    {
        var requests = new[]
        {
            BuildRequest(PaymentMethod.PixOnline),
            BuildRequest(PaymentMethod.CashOnDelivery, expectedTotal: -10m),
            BuildRequest(PaymentMethod.PixOnDelivery, expectedTotal: -10m),
            BuildCashRequest(withoutChange: false, changeFor: 50.001m)
        };

        foreach (var request in requests)
        {
            var result = await _validator.ValidateAsync(request);

            result.Errors.Should().NotBeEmpty();
            result.Errors.Select(x => x.ErrorMessage).Should().OnlyContain(message => !HasCorruptedCharacters(message),
                "validation messages are user facing and must not contain mojibake");
        }
    }

    private static CheckoutRequestDto BuildCashRequest(bool withoutChange, decimal? changeFor)
    {
        return new CheckoutRequestDto
        {
            StoreId = Guid.NewGuid(),
            FulfillmentType = FulfillmentType.PickUp,
            PaymentMethod = PaymentMethod.CashOnDelivery,
            ExpectedTotal = 40m,
            CashWithoutChange = withoutChange,
            CashChangeForAmount = changeFor,
            Items = [new CheckoutItemRequestDto { ProductId = Guid.NewGuid(), Quantity = 1 }]
        };
    }

    private static CheckoutRequestDto BuildRequest(
        PaymentMethod? paymentMethod,
        decimal? expectedTotal = null)
    {
        return new CheckoutRequestDto
        {
            StoreId = Guid.NewGuid(),
            FulfillmentType = FulfillmentType.PickUp,
            PaymentMethod = paymentMethod,
            ExpectedTotal = expectedTotal,
            Items = [new CheckoutItemRequestDto { ProductId = Guid.NewGuid(), Quantity = 1 }]
        };
    }

    // Latin Extended-B / IPA and the replacement character never belong to Portuguese copy; their
    // presence means the file was transcoded incorrectly.
    private static bool HasCorruptedCharacters(string value) =>
        value.Any(character => character == '\uFFFD' || (character >= '\u01C0' && character <= '\u02FF'));
}
