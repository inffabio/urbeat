using FluentAssertions;
using Urbeat.Domain.Entities;
using Urbeat.Domain.Services;

namespace Urbeat.UnitTests.Domain;

public sealed class OrderPaymentPolicyTests
{
    [Theory]
    [InlineData(PaymentMethod.CashOnDelivery, true)]
    [InlineData(PaymentMethod.PixOnDelivery, true)]
    [InlineData(PaymentMethod.DebitOnDelivery, true)]
    [InlineData(PaymentMethod.CreditOnDelivery, true)]
    [InlineData(PaymentMethod.PixOnline, false)]
    [InlineData(PaymentMethod.CardOnline, false)]
    [InlineData(PaymentMethod.CardOnDelivery, false)]
    public void SupportsNewOrder_ShouldOnlyAcceptTheCurrentStorefrontMethods(
        PaymentMethod method,
        bool expected)
    {
        OrderPaymentPolicy.SupportsNewOrder(method).Should().Be(expected);
    }

    [Fact]
    public void RequiresHandoverConfirmation_ShouldBeTrueForCurrentPayOnDeliveryContracts()
    {
        foreach (var method in new[]
                 {
                     PaymentMethod.PixOnDelivery,
                     PaymentMethod.DebitOnDelivery,
                     PaymentMethod.CreditOnDelivery
                 })
        {
            var order = new Order { PaymentMethod = method };
            OrderPaymentPolicy.RequiresHandoverConfirmation(order).Should().BeTrue(method.ToString());
        }

        var newCash = new Order
        {
            PaymentMethod = PaymentMethod.CashOnDelivery,
            CashWithoutChange = true
        };
        OrderPaymentPolicy.RequiresHandoverConfirmation(newCash).Should().BeTrue();
    }

    [Fact]
    public void RequiresHandoverConfirmation_ShouldKeepLegacyOrdersOnThePreviousFlow()
    {
        var legacyCash = new Order { PaymentMethod = PaymentMethod.CashOnDelivery };
        var legacyCard = new Order { PaymentMethod = PaymentMethod.CardOnDelivery };
        var legacyPix = new Order { PaymentMethod = PaymentMethod.PixOnline };

        OrderPaymentPolicy.RequiresHandoverConfirmation(legacyCash).Should().BeFalse();
        OrderPaymentPolicy.RequiresHandoverConfirmation(legacyCard).Should().BeFalse();
        OrderPaymentPolicy.RequiresHandoverConfirmation(legacyPix).Should().BeFalse();
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, "40.00")]
    [InlineData(false, "50.00")]
    public void ValidateCashSelection_ShouldAcceptCompleteChoices(bool withoutChange, string? changeFor)
    {
        var parsed = changeFor is null ? (decimal?)null : decimal.Parse(changeFor, System.Globalization.CultureInfo.InvariantCulture);

        OrderPaymentPolicy.ValidateCashSelection(40m, withoutChange, parsed).Should().BeNull();
    }

    [Fact]
    public void ValidateCashSelection_ShouldRejectMissingChoice()
    {
        OrderPaymentPolicy.ValidateCashSelection(40m, null, null).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ValidateCashSelection_ShouldRejectChangeBelowTotal()
    {
        OrderPaymentPolicy.ValidateCashSelection(40m, false, 39.99m).Should().Contain("total");
    }

    [Fact]
    public void ValidateCashSelection_ShouldRejectChangeWithMoreThanTwoDecimals()
    {
        OrderPaymentPolicy.ValidateCashSelection(40m, false, 50.001m).Should().Contain("duas casas");
    }

    [Fact]
    public void ValidateCashSelection_ShouldRejectInconsistentChoice()
    {
        OrderPaymentPolicy.ValidateCashSelection(40m, true, 50m).Should().NotBeNullOrWhiteSpace();
        OrderPaymentPolicy.ValidateCashSelection(40m, false, null).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ValidateCashReceived_ShouldComputeChangeFromTheAuthoritativeTotal()
    {
        var order = new Order
        {
            PaymentMethod = PaymentMethod.CashOnDelivery,
            Total = 37.50m,
            CashWithoutChange = false,
            CashChangeForAmount = 50m
        };

        var error = OrderPaymentPolicy.ValidateCashReceived(order, 50m, out var change);

        error.Should().BeNull();
        change.Should().Be(12.50m);
    }

    [Fact]
    public void ValidateCashReceived_ShouldRejectAmountBelowTotal()
    {
        var order = new Order { PaymentMethod = PaymentMethod.CashOnDelivery, Total = 37.50m };

        var error = OrderPaymentPolicy.ValidateCashReceived(order, 37.49m, out _);

        error.Should().Contain("menor que o total");
    }

    [Fact]
    public void ValidateCashReceived_ShouldAllowAmountAboveDeclaredChangeForAndComputeActualChange()
    {
        var order = new Order
        {
            PaymentMethod = PaymentMethod.CashOnDelivery,
            Total = 37.50m,
            CashWithoutChange = false,
            CashChangeForAmount = 50m
        };

        var error = OrderPaymentPolicy.ValidateCashReceived(order, 60m, out var change);

        error.Should().BeNull("the checkout forecast is not a cap");
        change.Should().Be(22.50m);
    }

    [Fact]
    public void ValidateCashReceived_ShouldAllowAmountAboveTotalWhenCustomerChoseNoChange()
    {
        var order = new Order
        {
            PaymentMethod = PaymentMethod.CashOnDelivery,
            Total = 37.50m,
            CashWithoutChange = true
        };

        var error = OrderPaymentPolicy.ValidateCashReceived(order, 50m, out var change);

        error.Should().BeNull("the real amount received may require change even if the customer said none");
        change.Should().Be(12.50m);
    }

    [Fact]
    public void ValidateCashReceived_ShouldRejectMissingAmountAndInvalidPrecision()
    {
        var order = new Order { PaymentMethod = PaymentMethod.CashOnDelivery, Total = 10m };

        OrderPaymentPolicy.ValidateCashReceived(order, null, out _).Should().NotBeNullOrWhiteSpace();
        OrderPaymentPolicy.ValidateCashReceived(order, 10.001m, out _).Should().Contain("duas casas");
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData(null, true, false)]
    [InlineData(null, false, false)]
    [InlineData(40.0, null, false)]
    [InlineData(40.0, false, false)]
    public void IsLegacyCashCheckout_ShouldOnlyMatchPayloadsWithoutAnyNewContractField(
        double? expectedTotal,
        bool? cashWithoutChange,
        bool expected)
    {
        var total = expectedTotal.HasValue ? (decimal?)expectedTotal.Value : null;

        OrderPaymentPolicy.IsLegacyCashCheckout(total, cashWithoutChange).Should().Be(expected);
    }
}
