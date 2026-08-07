namespace Urbeat.Application.DTOs;

public sealed class RegistrationResultDto
{
    public bool Succeeded { get; init; }

    public Guid? UserId { get; init; }

    public bool EmailConfirmationPending { get; init; }

    public bool DocumentAlreadyRegistered { get; init; }

    public string? ExistingUserEmail { get; init; }

    public IReadOnlyCollection<string> Errors { get; init; } = [];
}