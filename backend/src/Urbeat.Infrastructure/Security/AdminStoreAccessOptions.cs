using Microsoft.Extensions.Options;

namespace Urbeat.Infrastructure.Security;

/// <summary>
/// Non-secret operational settings for administrative store access. Values come from the
/// <c>AdminStoreAccess</c> configuration section; no new secret is introduced.
/// </summary>
public sealed class AdminStoreAccessOptions
{
    public const string SectionName = "AdminStoreAccess";

    /// <summary>Absolute lifetime of a context, in minutes. Default 30.</summary>
    public int ContextLifetimeMinutes { get; set; } = 30;

    /// <summary>Maximum number of simultaneously active contexts per administrator. Default 5.</summary>
    public int MaxActiveContextsPerAdmin { get; set; } = 5;

    /// <summary>How long revoked/expired contexts are retained for audit correlation. Default 24 h.</summary>
    public int ExpiredRetentionHours { get; set; } = 24;

    /// <summary>Minimum interval between <c>LastSeenAtUtc</c> writes. Default 60 s.</summary>
    public int TouchIntervalSeconds { get; set; } = 60;

    /// <summary>Upper bound for the administrative listing page size. Default 100.</summary>
    public int MaxPageSize { get; set; } = 100;
}

public sealed class AdminStoreAccessOptionsValidator : IValidateOptions<AdminStoreAccessOptions>
{
    public ValidateOptionsResult Validate(string? name, AdminStoreAccessOptions options)
    {
        var errors = new List<string>();

        if (options.ContextLifetimeMinutes is < 5 or > 120)
        {
            errors.Add("AdminStoreAccess:ContextLifetimeMinutes must be between 5 and 120.");
        }

        if (options.MaxActiveContextsPerAdmin is < 1 or > 20)
        {
            errors.Add("AdminStoreAccess:MaxActiveContextsPerAdmin must be between 1 and 20.");
        }

        if (options.ExpiredRetentionHours is < 1 or > 168)
        {
            errors.Add("AdminStoreAccess:ExpiredRetentionHours must be between 1 and 168.");
        }

        if (options.TouchIntervalSeconds is < 10 or > 300)
        {
            errors.Add("AdminStoreAccess:TouchIntervalSeconds must be between 10 and 300.");
        }

        if (options.MaxPageSize is < 1 or > 500)
        {
            errors.Add("AdminStoreAccess:MaxPageSize must be between 1 and 500.");
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}
