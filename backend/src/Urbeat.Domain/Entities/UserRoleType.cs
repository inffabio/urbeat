namespace Urbeat.Domain.Entities;

/// <summary>
/// Roles that own an independent password. Admin and DeliveryDriver accounts keep using the
/// legacy single-password flow and never get a <see cref="UserRoleCredential"/> row.
/// </summary>
public enum UserRoleType
{
    Customer = 1,
    Seller = 2
}
