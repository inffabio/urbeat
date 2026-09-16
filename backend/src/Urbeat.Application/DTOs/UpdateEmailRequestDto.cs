namespace Urbeat.Application.DTOs;

public sealed class UpdateEmailRequestDto
{
    public Guid UserId { get; init; }
    public string CurrentEmail { get; init; } = string.Empty;
    public string NewEmail { get; init; } = string.Empty;

    /// <summary>
    /// Signed, expiring, single-use challenge issued when the account was registered. It is the
    /// credential for this endpoint; <see cref="UserId"/> and <see cref="CurrentEmail"/> are not
    /// sufficient on their own.
    /// </summary>
    public string EmailChangeChallenge { get; init; } = string.Empty;
}
