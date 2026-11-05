namespace Urbeat.Application.SystemParameters;

/// <summary>
/// Authoritative allowlist of the system parameter keys that the platform
/// business settings screen may read and edit.
/// <para>
/// Technical, infrastructure, security and integration parameters (for example
/// <c>Security</c>/<c>Jwt</c>, SMTP, URLs, HTTP resilience, uploads, payment
/// gateway internals, Hangfire and CORS) are intentionally absent. They remain
/// in the database and available to <c>SystemParameterService</c>, but are
/// neither listed nor writable through the business settings endpoints.
/// </para>
/// </summary>
public static class BusinessSystemParameterCatalog
{
    /// <summary>Keys exposed by the business settings endpoints, in display order.</summary>
    public static readonly string[] Keys =
    [
        "Customer.MaxAddresses",
        "Order.DefaultPageSize",
        "Order.MaxPageSize",
        "Order.CodePrefix",
        "Order.CodeLength",
        "Order.CodeChars",
        "Order.CodeMaxAttempts",
        "Notification.MaxReturned",
        "Store.DefaultDeliveryFee",
        "Store.DefaultMinimumOrderValue",
        "Subscription.DueSoonDays",
        "Subscription.DefaultBillingCycleDays",
        "Subscription.BillingType",
        "Subscription.Cycle",
        "Subscription.Description"
    ];

    private static readonly HashSet<string> KeySet = new(Keys, StringComparer.Ordinal);

    public static bool Contains(string? key) =>
        !string.IsNullOrWhiteSpace(key) && KeySet.Contains(key);
}
