namespace Urbeat.Application.DTOs;

public sealed class CityResponseDto
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Uf { get; init; } = string.Empty;
}
