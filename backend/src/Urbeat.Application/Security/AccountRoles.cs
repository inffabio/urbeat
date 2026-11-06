using Urbeat.Domain.Entities;

namespace Urbeat.Application.Security;

/// <summary>
/// Names of the roles that own an independent password. Admin and DeliveryDriver are
/// intentionally absent: they keep the legacy identity password.
/// </summary>
public static class AccountRoles
{
    public const string Customer = "Customer";
    public const string Seller = "Seller";

    public static bool TryParseCredentialRole(string? value, out UserRoleType role)
    {
        if (string.Equals(value?.Trim(), Customer, StringComparison.OrdinalIgnoreCase))
        {
            role = UserRoleType.Customer;
            return true;
        }

        if (string.Equals(value?.Trim(), Seller, StringComparison.OrdinalIgnoreCase))
        {
            role = UserRoleType.Seller;
            return true;
        }

        role = default;
        return false;
    }

    public static string ToRoleName(UserRoleType role) => role switch
    {
        UserRoleType.Customer => Customer,
        UserRoleType.Seller => Seller,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unsupported credential role.")
    };
}
