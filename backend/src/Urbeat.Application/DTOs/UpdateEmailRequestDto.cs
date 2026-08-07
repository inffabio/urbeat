namespace Urbeat.Application.DTOs;

public sealed class UpdateEmailRequestDto
{
    public Guid UserId { get; init; }
    public string CurrentEmail { get; init; } = string.Empty;
    public string NewEmail { get; init; } = string.Empty;
}
