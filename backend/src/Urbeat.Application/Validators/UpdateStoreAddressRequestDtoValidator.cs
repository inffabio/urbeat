using FluentValidation;
using Urbeat.Application.DTOs;

namespace Urbeat.Application.Validators;

public sealed class UpdateStoreAddressRequestDtoValidator : AbstractValidator<UpdateStoreAddressRequestDto>
{
    public UpdateStoreAddressRequestDtoValidator()
    {
        RuleFor(x => x.Street)
            .NotEmpty()
            .MaximumLength(120);

        RuleFor(x => x.Number)
            .NotEmpty()
            .MaximumLength(20);

        RuleFor(x => x.Neighborhood)
            .NotEmpty()
            .MaximumLength(80);

        RuleFor(x => x.City)
            .NotEmpty()
            .MaximumLength(80);

        RuleFor(x => x.State)
            .NotEmpty()
            .MaximumLength(2);

        RuleFor(x => x.ZipCode)
            .NotEmpty()
            .MaximumLength(12);

        RuleFor(x => x.Complement)
            .MaximumLength(120)
            .When(x => !string.IsNullOrWhiteSpace(x.Complement));

        RuleFor(x => x.Reference)
            .MaximumLength(200)
            .When(x => !string.IsNullOrWhiteSpace(x.Reference));
    }
}