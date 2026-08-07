namespace Urbeat.Application.DTOs;

public sealed class ViaCepAddressResponseDto
{
    public string Cep { get; init; } = string.Empty;

    public string Street { get; init; } = string.Empty;

    public string Neighborhood { get; init; } = string.Empty;

    public string City { get; init; } = string.Empty;

    public string State { get; init; } = string.Empty;

    public string? Complement { get; init; }
}
