using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Urbeat.Infrastructure.Services.WhatsApp;

/// <summary>
/// Validates WhatsApp configuration without ever echoing secret values. Secrets are required only
/// when <see cref="WhatsAppOptions.WebhookEnabled"/> is explicitly set, so a deployment without
/// WhatsApp credentials still starts and the webhook endpoints fail closed (403/401).
/// </summary>
public sealed partial class WhatsAppOptionsValidator : IValidateOptions<WhatsAppOptions>
{
    public ValidateOptionsResult Validate(string? name, WhatsAppOptions options)
    {
        var failures = new List<string>();

        if (!GraphApiVersionRegex().IsMatch(options.GraphApiVersion))
        {
            failures.Add($"{WhatsAppOptions.SectionName}:GraphApiVersion must use the 'v<major>.<minor>' format.");
        }

        if (!Uri.TryCreate(options.GraphApiBaseUrl, UriKind.Absolute, out var baseUri)
            || baseUri.Scheme != Uri.UriSchemeHttps)
        {
            failures.Add($"{WhatsAppOptions.SectionName}:GraphApiBaseUrl must be an absolute https URL.");
        }

        if (options.TimeoutSeconds is < 1 or > 120)
        {
            failures.Add($"{WhatsAppOptions.SectionName}:TimeoutSeconds must be between 1 and 120.");
        }

        if (!string.IsNullOrWhiteSpace(options.PhoneNumberId) && !PhoneNumberIdRegex().IsMatch(options.PhoneNumberId))
        {
            failures.Add($"{WhatsAppOptions.SectionName}:PhoneNumberId must contain digits only.");
        }

        if (!string.IsNullOrWhiteSpace(options.MetaAppId) && !MetaAppIdRegex().IsMatch(options.MetaAppId))
        {
            failures.Add($"{WhatsAppOptions.SectionName}:MetaAppId must contain digits only.");
        }

        if (!string.IsNullOrWhiteSpace(options.ProfilePictureUrl)
            && !WhatsAppMediaUrl.TryValidate(options.ProfilePictureUrl, out _))
        {
            failures.Add($"{WhatsAppOptions.SectionName}:ProfilePictureUrl must be an absolute public https URL.");
        }

        if (options.WebhookEnabled)
        {
            if (string.IsNullOrWhiteSpace(options.WebhookVerifyToken))
            {
                failures.Add(
                    $"{WhatsAppOptions.SectionName}:WebhookVerifyToken is required when WebhookEnabled is true.");
            }

            if (string.IsNullOrWhiteSpace(options.AppSecret))
            {
                failures.Add(
                    $"{WhatsAppOptions.SectionName}:AppSecret is required when WebhookEnabled is true.");
            }

            if (string.IsNullOrWhiteSpace(options.PhoneNumberId))
            {
                failures.Add(
                    $"{WhatsAppOptions.SectionName}:PhoneNumberId is required when WebhookEnabled is true.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    [GeneratedRegex(@"^v\d+\.\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex GraphApiVersionRegex();

    [GeneratedRegex(@"^\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex PhoneNumberIdRegex();

    [GeneratedRegex(@"^\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex MetaAppIdRegex();
}
