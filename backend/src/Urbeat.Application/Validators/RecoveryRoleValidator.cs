using FluentValidation;
using Urbeat.Application.Security;

namespace Urbeat.Application.Validators;

internal static class RecoveryRoleValidator
{
    public static IRuleBuilderOptions<T, string> ValidRecoveryRole<T>(this IRuleBuilder<T, string> rule)
    {
        return rule
            .NotEmpty().WithMessage("Informe o tipo de acesso (Customer ou Seller).")
            .Must(IsCredentialRole)
            .WithMessage("Tipo de acesso inválido. Use Customer ou Seller.");
    }

    public static bool IsCredentialRole(string? value)
        => AccountRoles.TryParseCredentialRole(value, out _);
}
