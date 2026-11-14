namespace Urbeat.Application.DTOs;

/// <summary>
/// Result of a CEP lookup. The original ViaCEP response fields are preserved; the additional
/// fields are additive and expose only aggregated street coordinates, never an individual address
/// point. <see cref="Source"/> makes it explicit whether the answer came from the local geographic
/// base or from the ViaCEP fallback.
/// </summary>
public sealed class AddressLookupResponseDto
{
    public string Cep { get; init; } = string.Empty;

    public string Street { get; init; } = string.Empty;

    public string Neighborhood { get; init; } = string.Empty;

    public string City { get; init; } = string.Empty;

    public string State { get; init; } = string.Empty;

    public string? Complement { get; init; }

    public string? IbgeCode { get; init; }

    /// <summary>Mean coordinate of the matched logradouro(s). Never a household point.</summary>
    public double? Latitude { get; init; }

    public double? Longitude { get; init; }

    /// <summary><c>local</c> or <c>viacep</c>.</summary>
    public string Source { get; init; } = "local";

    /// <summary>Active CNEFE dataset version; null when the answer comes only from ViaCEP.</summary>
    public string? DatasetVersion { get; init; }

    /// <summary>True when the CEP covers more than one logradouro.</summary>
    public bool MultipleMatches { get; init; }

    public IReadOnlyList<AddressLookupMatchDto> Matches { get; init; } = [];
}

public sealed class AddressLookupMatchDto
{
    public string Street { get; init; } = string.Empty;

    public string Neighborhood { get; init; } = string.Empty;

    public string City { get; init; } = string.Empty;

    public string State { get; init; } = string.Empty;

    public string? IbgeCode { get; init; }

    public double? Latitude { get; init; }

    public double? Longitude { get; init; }
}

public sealed class StreetSearchResultDto
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? Neighborhood { get; init; }

    public string City { get; init; } = string.Empty;

    public string State { get; init; } = string.Empty;

    public double? Latitude { get; init; }

    public double? Longitude { get; init; }
}
