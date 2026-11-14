using Urbeat.Application.DTOs;

namespace Urbeat.Application.Interfaces;

/// <summary>
/// Address lookup backed by the local CNEFE-derived geographic base, with ViaCEP as a read-only
/// fallback. The local base is authoritative; ViaCEP results are never written back automatically.
/// </summary>
public interface IAddressLookupService
{
    Task<AddressLookupResponseDto?> LookupByCepAsync(string cep, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches logradouros by prefix inside a single city (or UF when the city is unknown). A
    /// national free-text scan is never allowed.
    /// </summary>
    Task<IReadOnlyCollection<StreetSearchResultDto>> SearchStreetsAsync(
        Guid? cityId,
        string? uf,
        string? query,
        int limit = 20,
        CancellationToken cancellationToken = default);
}
