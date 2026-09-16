namespace Urbeat.Application.Interfaces;

/// <summary>
/// Result of validating an e-mail change challenge. A valid challenge proves that the caller holds
/// a short-lived, signed token issued when the account was registered (or promoted), and binds the
/// change to the user and the e-mail that existed at issuance.
/// </summary>
public sealed class EmailChangeChallengeValidation
{
    public bool Valid { get; init; }

    public Guid UserId { get; init; }

    public string Email { get; init; } = string.Empty;

    public string SecurityStamp { get; init; } = string.Empty;
}

public interface IEmailChangeChallengeService
{
    string CreateChallenge(Guid userId, string email, string securityStamp);

    EmailChangeChallengeValidation ValidateChallenge(string challenge);
}
