using FluentValidation;
using Urbeat.Application.DTOs;

namespace Urbeat.Application.Validators;

public sealed class ForgotPasswordRequestDtoValidator : AbstractValidator<ForgotPasswordRequestDto>
{
    public ForgotPasswordRequestDtoValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Informe um e-mail válido.")
            .EmailAddress().WithMessage("Informe um e-mail válido.");

        RuleFor(x => x.Role).ValidRecoveryRole();
    }
}
