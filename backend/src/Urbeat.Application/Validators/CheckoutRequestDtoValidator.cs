using FluentValidation;
using Urbeat.Application.DTOs;
using Urbeat.Domain.Entities;
using Urbeat.Domain.Services;

namespace Urbeat.Application.Validators;

public sealed class CheckoutRequestDtoValidator : AbstractValidator<CheckoutRequestDto>
{
    public CheckoutRequestDtoValidator()
    {
        RuleFor(x => x.StoreId)
            .NotEmpty();

        RuleFor(x => x.FulfillmentType)
            .IsInEnum();

        // Address and Payment are only strictly required when confirming an order (PaymentMethod is provided)
        // For preview, they can be omitted.
        RuleFor(x => x.CustomerAddressId)
            .NotEmpty()
            .When(x => x.FulfillmentType == FulfillmentType.Delivery && x.PaymentMethod.HasValue);

        RuleFor(x => x.CustomerAddressId)
            .Null()
            .When(x => x.FulfillmentType == FulfillmentType.PickUp && x.PaymentMethod.HasValue);

        RuleFor(x => x.PaymentMethod)
            .IsInEnum()
            .When(x => x.PaymentMethod.HasValue);

        RuleFor(x => x.ExpectedTotal)
            .Must(value => !value.HasValue || OrderPaymentPolicy.HasCurrencyPrecision(value.Value))
            .WithMessage("O total esperado deve ter no máximo duas casas decimais.")
            .When(x => x.ExpectedTotal.HasValue);

        RuleFor(x => x.ExpectedTotal)
            .GreaterThanOrEqualTo(0m)
            .WithMessage("O total esperado não pode ser negativo.")
            .When(x => x.ExpectedTotal.HasValue);

        // Online Pix and the legacy generic card-on-delivery are no longer offered. Older cached
        // storefronts (PWA service worker or Capacitor webview) may still send them, so the message
        // tells the customer how to recover instead of exposing an internal policy code.
        RuleFor(x => x.PaymentMethod)
            .Must(method => !method.HasValue || OrderPaymentPolicy.SupportsNewOrder(method.Value))
            .WithMessage("Esta forma de pagamento não está mais disponível. Atualize o aplicativo ou recarregue a página e escolha Pix na maquininha, dinheiro, débito ou crédito.")
            .When(x => x.PaymentMethod.HasValue);

        RuleFor(x => x.CashWithoutChange)
            .Must((root, value) => !value.HasValue || root.PaymentMethod == PaymentMethod.CashOnDelivery)
            .WithMessage("A escolha de troco só é válida para pagamento em dinheiro.")
            .When(x => x.CashWithoutChange.HasValue);

        RuleFor(x => x.CashChangeForAmount)
            .Must(value => !value.HasValue || OrderPaymentPolicy.HasCurrencyPrecision(value.Value))
            .WithMessage("O valor para troco deve ter no máximo duas casas decimais.")
            .When(x => x.CashChangeForAmount.HasValue);

        RuleFor(x => x.CashChangeForAmount)
            .GreaterThan(0m)
            .WithMessage("O valor para troco deve ser maior que zero.")
            .When(x => x.CashChangeForAmount.HasValue);

        RuleFor(x => x.CashChangeForAmount)
            .LessThanOrEqualTo(OrderPaymentPolicy.MaxCashAmount)
            .WithMessage("O valor para troco excede o limite permitido.")
            .When(x => x.CashChangeForAmount.HasValue);

        RuleFor(x => x.CashChangeForAmount)
            .Null()
            .WithMessage("Informe a forma de pagamento em dinheiro para declarar troco.")
            .When(x => x.CashChangeForAmount.HasValue && x.PaymentMethod != PaymentMethod.CashOnDelivery);

        RuleFor(x => x.Notes)
            .MaximumLength(500)
            .When(x => !string.IsNullOrWhiteSpace(x.Notes));

        RuleFor(x => x.Items)
            .NotEmpty();

        RuleForEach(x => x.Items)
            .ChildRules(item =>
            {
                item.RuleFor(x => x.ProductId)
                    .NotEmpty()
                    .WithMessage("Produto inválido no item do pedido.");

                item.RuleFor(x => x.Quantity)
                    .GreaterThan(0)
                    .WithMessage("A quantidade deve ser maior que zero.");
            });
    }
}
