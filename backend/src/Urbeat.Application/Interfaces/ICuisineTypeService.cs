using Urbeat.Application.DTOs;

namespace Urbeat.Application.Interfaces;

public interface ICuisineTypeService
{
    Task<IReadOnlyCollection<CuisineTypeResponseDto>> GetActiveAsync(CancellationToken cancellationToken = default);
    Task<CuisineTypeResponseDto> CreateAsync(string name, CancellationToken cancellationToken = default);
}