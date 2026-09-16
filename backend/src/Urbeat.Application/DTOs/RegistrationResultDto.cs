namespace Urbeat.Application.DTOs;

public sealed class RegistrationResultDto
{
    public bool Succeeded { get; init; }

    public Guid? UserId { get; init; }

    public bool EmailConfirmationPending { get; init; }

    public bool DocumentAlreadyRegistered { get; init; }

    public bool ContractorNameAlreadyRegistered { get; init; }

    public string? ExistingUserEmail { get; init; }

    /// <summary>
    /// Short-lived signed challenge that authorizes changing the pending e-mail before the account
    /// is confirmed. Issued at registration/promotion and required by the update-email endpoint.
    /// </summary>
    public string? EmailChangeChallenge { get; init; }

    public IReadOnlyCollection<string> Errors { get; init; } = [];
}