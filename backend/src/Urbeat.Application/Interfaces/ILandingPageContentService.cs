using Urbeat.Application.Dtos;

namespace Urbeat.Application.Interfaces;

public interface ILandingPageContentService
{
    Task<List<LandingPageContentResponseDto>> GetAllAsync(CancellationToken cancellationToken);
    Task<List<LandingPageContentResponseDto>> GetBySectionAsync(string section, CancellationToken cancellationToken);
    Task<LandingPageContentResponseDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<LandingPageContentResponseDto> CreateAsync(LandingPageContentRequestDto request, CancellationToken cancellationToken);
    Task<LandingPageContentResponseDto> UpdateAsync(Guid id, LandingPageContentRequestDto request, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
