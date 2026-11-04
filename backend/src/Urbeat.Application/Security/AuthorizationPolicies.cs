namespace Urbeat.Application.Security;

public static class AuthorizationPolicies
{
    public const string AdminOnly = nameof(AdminOnly);

    public const string SellerOnly = nameof(SellerOnly);

    public const string CustomerOnly = nameof(CustomerOnly);
}