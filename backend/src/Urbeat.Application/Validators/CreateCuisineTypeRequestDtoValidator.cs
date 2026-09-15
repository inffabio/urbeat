using FluentValidation;
using Urbeat.Application.DTOs;

namespace Urbeat.Application.Validators;

public sealed class CreateCuisineTypeRequestDtoValidator : AbstractValidator<CreateCuisineTypeRequestDto>
{
    public CreateCuisineTypeRequestDtoValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("O nome da categoria é obrigatório.")
            .MaximumLength(80)
            .WithMessage("O nome da categoria deve ter no máximo 80 caracteres.");
    }
}
