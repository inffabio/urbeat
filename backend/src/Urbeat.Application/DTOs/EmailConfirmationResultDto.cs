namespace Urbeat.Application.DTOs;

public sealed class EmailConfirmationResultDto
{
    public bool Succeeded { get; init; }

    public bool AlreadyConfirmed { get; init; }

    public bool UserNotFound { get; init; }

    public bool InvalidToken { get; init; }

    public IReadOnlyCollection<string> Errors { get; init; } = [];

    /// <summary>
    /// Optional client cooldown deadline for a resend request. It is a UX hint only: delivery is
    /// asynchronous and the server rate limiter remains authoritative. It is returned for unknown
    /// or already-confirmed e-mails as well, so it never discloses whether an account exists.
    /// </summary>
    public DateTime? ResendAvailableAtUtc { get; init; }
}
