using FluentValidation;
using Urbeat.Application.DTOs;

namespace Urbeat.Application.Validators;

public sealed class AdminRecoverPasswordRequestDtoValidator : AbstractValidator<AdminRecoverPasswordRequestDto>
{
    public AdminRecoverPasswordRequestDtoValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("O e-mail do administrador é obrigatório.")
            .EmailAddress().WithMessage("Informe um e-mail válido.")
            .MaximumLength(255).WithMessage("O e-mail deve ter no máximo 255 caracteres.");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("A nova senha é obrigatória.")
            .MinimumLength(8).WithMessage("A senha deve ter no mínimo 8 caracteres.")
            .MaximumLength(128).WithMessage("A senha deve ter no máximo 128 caracteres.")
            .Matches(@"[A-Z]").WithMessage("A senha deve conter pelo menos 1 letra maiúscula.")
            .Matches(@"[a-z]").WithMessage("A senha deve conter pelo menos 1 letra minúscula.")
            .Matches(@"\d").WithMessage("A senha deve conter pelo menos 1 número.")
            .Matches(@"[!@#$%^&*]").WithMessage("A senha deve conter pelo menos 1 caractere especial (!@#$%^&*).");

        RuleFor(x => x.ConfirmPassword)
            .Equal(x => x.NewPassword).WithMessage("As senhas não coincidem.");
    }
}
