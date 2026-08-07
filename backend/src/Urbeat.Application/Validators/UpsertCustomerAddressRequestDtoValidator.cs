using FluentValidation;
using Urbeat.Application.DTOs;

namespace Urbeat.Application.Validators;

public sealed class UpsertCustomerAddressRequestDtoValidator : AbstractValidator<UpsertCustomerAddressRequestDto>
{
    public UpsertCustomerAddressRequestDtoValidator()
    {
        RuleFor(x => x.Cep)
            .NotEmpty()
            .Matches("^[0-9]{8}$")
            .WithMessage("CEP must contain 8 digits.");

        RuleFor(x => x.Number)
            .NotEmpty()
            .MaximumLength(20);

        RuleFor(x => x.Street)
            .MaximumLength(120)
            .When(x => !string.IsNullOrWhiteSpace(x.Street));

        RuleFor(x => x.Neighborhood)
            .MaximumLength(80)
            .When(x => !string.IsNullOrWhiteSpace(x.Neighborhood));

        RuleFor(x => x.City)
            .MaximumLength(80)
            .When(x => !string.IsNullOrWhiteSpace(x.City));

        RuleFor(x => x.State)
            .MaximumLength(2)
            .When(x => !string.IsNullOrWhiteSpace(x.State));

        RuleFor(x => x.Complement)
            .MaximumLength(120)
            .When(x => !string.IsNullOrWhiteSpace(x.Complement));

        RuleFor(x => x.Reference)
            .MaximumLength(200)
            .When(x => !string.IsNullOrWhiteSpace(x.Reference));
    }
}
