using FluentValidation;
using Urbeat.Application.DTOs;

namespace Urbeat.Application.Validators;

public sealed class CreateOrderPaymentRequestDtoValidator : AbstractValidator<CreateOrderPaymentRequestDto>
{
    public CreateOrderPaymentRequestDtoValidator()
    {
        RuleFor(x => x.OrderId)
            .NotEmpty();
    }
}
