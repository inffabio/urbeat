using Microsoft.Extensions.Options;

namespace Urbeat.WebApi.Infrastructure;

/// <summary>
/// Validates the confirmation e-mail resend bucket at startup. The client cooldown hint
/// (<c>EmailConfirmation:ResendCooldownSeconds</c>) is an independent UX value, so the only
/// contradictory configuration is a non-positive limit or window: zero would make the
/// authoritative protection reject every request, and a negative value is meaningless.
/// </summary>
public sealed class RateLimitingOptionsValidator : IValidateOptions<RateLimitingOptions>
{
    public ValidateOptionsResult Validate(string? name, RateLimitingOptions options)
    {
        var errors = new List<string>();

        if (options.EmailResendPermitLimit < 1)
        {
            errors.Add(
                $"{RateLimitingOptions.SectionName}:EmailResendPermitLimit must be at least 1.");
        }

        if (options.EmailResendWindowSeconds < 1)
        {
            errors.Add(
                $"{RateLimitingOptions.SectionName}:EmailResendWindowSeconds must be at least 1.");
        }

        if (options.AdminRecoveryPermitLimit < 1)
        {
            errors.Add(
                $"{RateLimitingOptions.SectionName}:AdminRecoveryPermitLimit must be at least 1.");
        }

        if (options.AdminRecoveryWindowSeconds < 1)
        {
            errors.Add(
                $"{RateLimitingOptions.SectionName}:AdminRecoveryWindowSeconds must be at least 1.");
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}
