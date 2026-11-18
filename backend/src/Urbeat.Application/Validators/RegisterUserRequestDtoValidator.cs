using FluentValidation;
using Urbeat.Application.DTOs;

namespace Urbeat.Application.Validators;

public sealed class RegisterUserRequestDtoValidator : AbstractValidator<RegisterUserRequestDto>
{
    public RegisterUserRequestDtoValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty()
            .MinimumLength(3)
            .MaximumLength(150);

        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress();

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8);

        RuleFor(x => x.PhoneNumber)
            .MaximumLength(20)
            .When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber));

        RuleFor(x => x.Document)
            .Must(doc => DocumentValidator.IsCpfOrCnpjValid(doc))
            .WithMessage("CPF/CNPJ inválido!")
            .When(x => !string.IsNullOrWhiteSpace(x.Document));

        RuleFor(x => x.PlanId)
            .Must(planId => planId != Guid.Empty)
            .When(x => x.PlanId.HasValue)
            .WithMessage("Plano inválido.");

        RuleFor(x => x.BillingPeriod)
            .IsInEnum()
            .When(x => x.BillingPeriod.HasValue)
            .WithMessage("Periodicidade inválida.");
    }
}