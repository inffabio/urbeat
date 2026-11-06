using FluentValidation;
using Urbeat.Application.DTOs;

namespace Urbeat.Application.Validators;

public sealed class ChangePasswordRequestDtoValidator : AbstractValidator<ChangePasswordRequestDto>
{
    public ChangePasswordRequestDtoValidator()
    {
        RuleFor(x => x.Role).ValidRecoveryRole();

        RuleFor(x => x.CurrentPassword)
            .NotEmpty().WithMessage("Informe a senha atual do acesso.");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("A nova senha é obrigatória.")
            .MinimumLength(8).WithMessage("A senha deve ter no mínimo 8 caracteres.")
            .Matches(@"[A-Z]").WithMessage("A senha deve conter pelo menos 1 letra maiúscula.")
            .Matches(@"[a-z]").WithMessage("A senha deve conter pelo menos 1 letra minúscula.")
            .Matches(@"\d").WithMessage("A senha deve conter pelo menos 1 número.")
            .Matches(@"[!@#$%^&*]").WithMessage("A senha deve conter pelo menos 1 caractere especial (!@#$%^&*).");

        RuleFor(x => x.ConfirmPassword)
            .Equal(x => x.NewPassword).WithMessage("As senhas não coincidem.");
    }
}
