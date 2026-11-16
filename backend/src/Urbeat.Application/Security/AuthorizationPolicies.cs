namespace Urbeat.Application.Security;

public static class AuthorizationPolicies
{
    public const string AdminOnly = nameof(AdminOnly);

    public const string SellerOnly = nameof(SellerOnly);

    public const string CustomerOnly = nameof(CustomerOnly);

    /// <summary>
    /// Store-scoped operations. A seller session keeps the historical ownership behaviour; an
    /// administrator only passes when a valid, matching server-side store context is attached.
    /// </summary>
    public const string StoreAccess = nameof(StoreAccess);
}