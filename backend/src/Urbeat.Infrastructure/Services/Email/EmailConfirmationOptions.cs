namespace Urbeat.Infrastructure.Services.Email;

public sealed class EmailConfirmationOptions
{
    public const string SectionName = "EmailConfirmation";

    public string FrontendBaseUrl { get; set; } = "http://localhost:4200";

    public string ConfirmPath { get; set; } = "/confirm-email";

    /// <summary>Frontend page that confirms a pending second-role promotion.</summary>
    public string RolePromotionPath { get; set; } = "/confirmacao-email";

    /// <summary>
    /// Client cooldown hint, in seconds, returned after an accepted resend request. It is a UX
    /// hint only: it does not represent delivery and it can be omitted (zero) without weakening the
    /// endpoint. It is an independent protection from
    /// <c>RateLimiting:EmailResendWindowSeconds</c>, which is the authoritative server-side limit
    /// for repeated resend requests; the two values are intentionally not derived from each other,
    /// and the rate limiter is the one that actually protects the endpoint.
    /// </summary>
    public int ResendCooldownSeconds { get; set; } = 120;
}
