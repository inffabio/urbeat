namespace Urbeat.Application.DTOs;

public sealed class ConfirmEmailRequestDto
{
    public Guid UserId { get; init; }

    public string Token { get; init; } = string.Empty;
}
