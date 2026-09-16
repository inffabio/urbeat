namespace Urbeat.WebApi.Infrastructure;

/// <summary>
/// Configuration for the request rate limits applied to sensitive authentication endpoints.
/// Values are read from the <c>RateLimiting</c> configuration section.
/// </summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Permit limit for general authentication endpoints (login, register, refresh).</summary>
    public int AuthPermitLimit { get; set; } = 30;

    /// <summary>Window, in seconds, for the general authentication limit.</summary>
    public int AuthWindowSeconds { get; set; } = 60;

    /// <summary>Permit limit for password recovery endpoints.</summary>
    public int PasswordRecoveryPermitLimit { get; set; } = 5;

    /// <summary>Window, in seconds, for the password recovery limit.</summary>
    public int PasswordRecoveryWindowSeconds { get; set; } = 300;
}
