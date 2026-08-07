namespace Urbeat.Application.DTOs;

public sealed class EmailConfirmationResultDto
{
    public bool Succeeded { get; init; }

    public bool AlreadyConfirmed { get; init; }

    public bool UserNotFound { get; init; }

    public bool InvalidToken { get; init; }

    public IReadOnlyCollection<string> Errors { get; init; } = [];
}
