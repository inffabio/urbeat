namespace Urbeat.Application.Interfaces;

using Urbeat.Application.DTOs;

public interface IOsmService
{
    Task<ImportNeighborhoodsResultDto> ImportNeighborhoodsByCepAsync(
        string cep,
        CancellationToken cancellationToken = default);

    Task<ImportNeighborhoodsResultDto> ImportNeighborhoodsByCityIdAsync(
        Guid cityId,
        CancellationToken cancellationToken = default);

    Task<ImportNeighborhoodsResultDto> ImportNeighborhoodsByCityNameAsync(
        string city,
        string uf,
        Guid? storeId = null,
        CancellationToken cancellationToken = default);

    Task<NeighborhoodMapResponseDto> GetNeighborhoodsMapAsync(
        Guid cityId,
        Guid? storeId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<NeighborhoodSearchResultDto>> SearchNeighborhoodsAsync(
        Guid cityId,
        Guid? storeId,
        string? search,
        bool activeOnly,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<CityResponseDto>> GetCitiesAsync(
        CancellationToken cancellationToken = default);

    Task<bool> HasNeighborhoodsForCityAsync(Guid cityId, CancellationToken cancellationToken = default);
}
