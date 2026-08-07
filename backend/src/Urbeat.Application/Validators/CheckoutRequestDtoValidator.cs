using FluentValidation;
using Urbeat.Application.DTOs;
using Urbeat.Domain.Entities;

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
