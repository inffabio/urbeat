namespace Urbeat.Application.DTOs;

public sealed class UpsertCustomerAddressRequestDto
{
    public string Cep { get; init; } = string.Empty;

    public string Number { get; init; } = string.Empty;

    public string? Street { get; init; }

    public string? Neighborhood { get; init; }

    public string? City { get; init; }

    public string? State { get; init; }

    public string? Complement { get; init; }

    public string? Reference { get; init; }

    public bool IsPrimary { get; init; }

    public double? Latitude { get; init; }

    public double? Longitude { get; init; }
}
