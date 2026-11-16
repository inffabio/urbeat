using FluentValidation;
using Urbeat.Application.DTOs;

namespace Urbeat.Application.Validators;

public sealed class InviteAdminAccountRequestDtoValidator : AbstractValidator<InviteAdminAccountRequestDto>
{
    public InviteAdminAccountRequestDtoValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("O e-mail do administrador é obrigatório.")
            .EmailAddress().WithMessage("Informe um e-mail válido.")
            .MaximumLength(254).WithMessage("O e-mail deve ter no máximo 254 caracteres.");
    }
}

public sealed class UpdateAdminEmailRequestDtoValidator : AbstractValidator<UpdateAdminEmailRequestDto>
{
    public UpdateAdminEmailRequestDtoValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("O e-mail do administrador é obrigatório.")
            .EmailAddress().WithMessage("Informe um e-mail válido.")
            .MaximumLength(254).WithMessage("O e-mail deve ter no máximo 254 caracteres.");
    }
}

public sealed class AdminAccountBlockRequestDtoValidator : AbstractValidator<AdminAccountBlockRequestDto>
{
    public AdminAccountBlockRequestDtoValidator()
    {
        RuleFor(x => x.Reason)
            .MaximumLength(200).WithMessage("O motivo deve ter no máximo 200 caracteres.");
    }
}

public sealed class AdminAccountTokenRequestDtoValidator : AbstractValidator<AdminAccountTokenRequestDto>
{
    public AdminAccountTokenRequestDtoValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("O token é obrigatório.")
            .MaximumLength(512).WithMessage("Token inválido.");
    }
}

public sealed class AdminAccountAcceptRequestDtoValidator : AbstractValidator<AdminAccountAcceptRequestDto>
{
    public AdminAccountAcceptRequestDtoValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("O token é obrigatório.")
            .MaximumLength(512).WithMessage("Token inválido.");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("A nova senha é obrigatória.")
            .MinimumLength(8).WithMessage("A senha deve ter no mínimo 8 caracteres.")
            .MaximumLength(128).WithMessage("A senha deve ter no máximo 128 caracteres.")
            .Matches(@"[A-Z]").WithMessage("A senha deve conter pelo menos 1 letra maiúscula.")
            .Matches(@"[a-z]").WithMessage("A senha deve conter pelo menos 1 letra minúscula.")
            .Matches(@"\d").WithMessage("A senha deve conter pelo menos 1 número.");

        RuleFor(x => x.ConfirmPassword)
            .Equal(x => x.NewPassword).WithMessage("As senhas não coincidem.");
    }
}
