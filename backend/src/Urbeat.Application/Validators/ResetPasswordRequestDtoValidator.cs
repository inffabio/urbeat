using FluentValidation;

namespace Urbeat.Application.Validators;

public sealed class ResetPasswordRequestDtoValidator : AbstractValidator<DTOs.ResetPasswordRequestDto>
{
    public ResetPasswordRequestDtoValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("O token é obrigatório.");

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
