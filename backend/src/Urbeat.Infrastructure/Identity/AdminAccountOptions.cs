using Microsoft.Extensions.Options;

namespace Urbeat.Infrastructure.Identity;

/// <summary>
/// Non-secret operational configuration of the administrative account links. The base URL comes
/// from <c>EmailConfirmation:FrontendBaseUrl</c>; no new secret is introduced, so no Vault entry is
/// required.
/// </summary>
public sealed class AdminAccountOptions
{
    public const string SectionName = "AdminAccount";

    public int InvitationLifetimeHours { get; set; } = 24;

    public int PasswordResetLifetimeMinutes { get; set; } = 60;

    public string InvitationPath { get; set; } = "/convite-admin";

    public string PasswordResetPath { get; set; } = "/redefinir-senha-admin";

    public int ClaimLeaseMinutes { get; set; } = 2;

    public int EmailSendLeaseMinutes { get; set; } = 5;

    public int MaxPageSize { get; set; } = 100;

    public TimeSpan InvitationLifetime => TimeSpan.FromHours(InvitationLifetimeHours);

    public TimeSpan PasswordResetLifetime => TimeSpan.FromMinutes(PasswordResetLifetimeMinutes);

    public TimeSpan ClaimLease => TimeSpan.FromMinutes(ClaimLeaseMinutes);

    public TimeSpan EmailSendLease => TimeSpan.FromMinutes(EmailSendLeaseMinutes);

    public IReadOnlyList<string> Validate()
    {
        var failures = new List<string>();

        if (InvitationLifetimeHours is < 1 or > 168)
        {
            failures.Add($"{SectionName}:InvitationLifetimeHours must be between 1 and 168.");
        }

        if (PasswordResetLifetimeMinutes is < 5 or > 1440)
        {
            failures.Add($"{SectionName}:PasswordResetLifetimeMinutes must be between 5 and 1440.");
        }

        if (ClaimLeaseMinutes is < 1 or > 15)
        {
            failures.Add($"{SectionName}:ClaimLeaseMinutes must be between 1 and 15.");
        }

        if (EmailSendLeaseMinutes is < 1 or > 15)
        {
            failures.Add($"{SectionName}:EmailSendLeaseMinutes must be between 1 and 15.");
        }

        if (MaxPageSize is < 1 or > 500)
        {
            failures.Add($"{SectionName}:MaxPageSize must be between 1 and 500.");
        }

        ValidatePath(failures, nameof(InvitationPath), InvitationPath);
        ValidatePath(failures, nameof(PasswordResetPath), PasswordResetPath);

        return failures;
    }

    private static void ValidatePath(List<string> failures, string property, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !value.StartsWith('/')
            || value.Contains('?')
            || value.Contains('#'))
        {
            failures.Add($"{SectionName}:{property} must start with '/' and must not contain '?' or '#'.");
        }
    }
}

public sealed class AdminAccountOptionsValidator : IValidateOptions<AdminAccountOptions>
{
    public ValidateOptionsResult Validate(string? name, AdminAccountOptions options)
    {
        var failures = options.Validate();
        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
