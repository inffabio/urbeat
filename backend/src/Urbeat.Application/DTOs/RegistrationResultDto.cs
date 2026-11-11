namespace Urbeat.Application.DTOs;

public sealed class RegistrationResultDto
{
    public bool Succeeded { get; init; }

    public Guid? UserId { get; init; }

    public bool EmailConfirmationPending { get; init; }

    public bool DocumentAlreadyRegistered { get; init; }

    /// <summary>
    /// True when an existing account received the requested role and a new, independent
    /// password instead of a brand-new identity being created.
    /// </summary>
    public bool RoleAddedToExistingAccount { get; init; }

    /// <summary>
    /// True when the requested role belongs to an existing identity but the caller is not
    /// authenticated as its owner. No role or credential is created until the e-mail challenge is
    /// confirmed; the response intentionally stays generic about the other role.
    /// </summary>
    public bool RolePromotionPending { get; init; }

    public string? ExistingUserEmail { get; init; }

    /// <summary>
    /// Short-lived signed challenge that authorizes changing the pending e-mail before the account
    /// is confirmed. Issued at registration/promotion and required by the update-email endpoint.
    /// </summary>
    public string? EmailChangeChallenge { get; init; }

    public IReadOnlyCollection<string> Errors { get; init; } = [];
}