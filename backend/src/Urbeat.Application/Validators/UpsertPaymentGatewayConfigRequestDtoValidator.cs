using FluentValidation;
using Urbeat.Application.DTOs;

namespace Urbeat.Application.Validators;

public sealed class UpsertPaymentGatewayConfigRequestDtoValidator : AbstractValidator<UpsertPaymentGatewayConfigRequestDto>
{
    public UpsertPaymentGatewayConfigRequestDtoValidator()
    {
        RuleFor(x => x.Gateway)
            .IsInEnum();

        RuleFor(x => x.AccessToken)
            .NotEmpty()
            .MaximumLength(500);

        RuleFor(x => x.NotificationUrl)
            .MaximumLength(500);

        RuleFor(x => x.Environment)
            .NotEmpty()
            .Must(x => x is "Sandbox" or "Production")
            .WithMessage("Environment must be 'Sandbox' or 'Production'.");
    }
}
