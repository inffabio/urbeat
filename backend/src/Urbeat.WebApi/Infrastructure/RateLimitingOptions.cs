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

    /// <summary>
    /// Permit limit for registration and role promotion confirmation. This flow can trigger an
    /// e-mail to an existing account, so it has its own stricter budget.
    /// </summary>
    public int RolePromotionPermitLimit { get; set; } = 20;

    /// <summary>Window, in seconds, for the role promotion limit.</summary>
    public int RolePromotionWindowSeconds { get; set; } = 300;

    /// <summary>
    /// Permit limit for confirmation e-mail resend requests. It is intentionally separate from the
    /// general auth budget so resend traffic neither consumes nor is throttled by login/register
    /// traffic. This limiter is the authoritative protection for repeated resends; the client
    /// cooldown hint (<c>EmailConfirmation:ResendCooldownSeconds</c>) is a distinct, non-binding UX
    /// value, so a client that honours the hint simply never reaches this limit.
    /// </summary>
    public int EmailResendPermitLimit { get; set; } = 3;

    /// <summary>
    /// Window, in seconds, for the confirmation e-mail resend limit. It is independent from the
    /// client cooldown hint (<c>EmailConfirmation:ResendCooldownSeconds</c>) on purpose: the hint may
    /// be shorter or longer than this window, and only this limiter keeps the endpoint safe. Both
    /// must stay positive; <see cref="RateLimitingOptionsValidator"/> fails startup otherwise.
    /// </summary>
    public int EmailResendWindowSeconds { get; set; } = 120;

    /// <summary>
    /// Permit limit for the break-glass administrative recovery endpoint. It is deliberately very
    /// small: a legitimate operation is one request, and the endpoint must not become a key oracle.
    /// </summary>
    public int AdminRecoveryPermitLimit { get; set; } = 5;

    /// <summary>Window, in seconds, for the administrative recovery limit.</summary>
    public int AdminRecoveryWindowSeconds { get; set; } = 900;

    /// <summary>Permit limit for the public tokenized route endpoints.</summary>
    public int PublicRoutePermitLimit { get; set; } = 120;

    /// <summary>Window, in seconds, for the public route limit.</summary>
    public int PublicRouteWindowSeconds { get; set; } = 60;
}
