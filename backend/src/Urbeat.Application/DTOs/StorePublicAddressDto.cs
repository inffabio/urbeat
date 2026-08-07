namespace Urbeat.Application.DTOs;

public sealed class StorePublicAddressDto
{
    public string Street { get; init; } = string.Empty;

    public string Number { get; init; } = string.Empty;

    public string Neighborhood { get; init; } = string.Empty;

    public string City { get; init; } = string.Empty;

    public string State { get; init; } = string.Empty;

    public string ZipCode { get; init; } = string.Empty;

    public string? Complement { get; init; }

    public string? Reference { get; init; }

    public double? Latitude { get; init; }

    public double? Longitude { get; init; }
}
