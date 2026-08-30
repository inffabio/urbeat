using FluentValidation;
using Urbeat.Application.DTOs;

namespace Urbeat.Application.Validators;

public sealed class UpdateCustomerProfileRequestDtoValidator : AbstractValidator<UpdateCustomerProfileRequestDto>
{
    public UpdateCustomerProfileRequestDtoValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MinimumLength(3).MaximumLength(120);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.PhoneNumber)
            .NotEmpty()
            .Must(phone => phone.Count(char.IsDigit) is >= 10 and <= 11)
            .WithMessage("Informe um telefone válido.");
    }
}
