using FluentValidation;
using Urbeat.Application.DTOs;

namespace Urbeat.Application.Validators;

public sealed class UpdateSellerProfileRequestDtoValidator : AbstractValidator<UpdateSellerProfileRequestDto>
{
    public UpdateSellerProfileRequestDtoValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty()
            .WithMessage("O nome do contratante é obrigatório.")
            .Must(name => !string.IsNullOrWhiteSpace(name) && name.Trim().Length >= 3)
            .WithMessage("O nome do contratante deve ter pelo menos 3 caracteres.")
            .MaximumLength(120)
            .WithMessage("O nome do contratante deve ter no máximo 120 caracteres.");
    }
}
