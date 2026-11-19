using Urbeat.Application.DTOs;

namespace Urbeat.Application.Interfaces;

public interface IPlanService
{
    Task<PagedPlanResponseDto> ListPagedAsync(
        PlanPageQueryDto query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlanResponseDto>> ListAllAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlanResponseDto>> ListActiveAsync(CancellationToken cancellationToken = default);

    Task<PublicPlanOfferResponseDto> GetPublicOfferAsync(CancellationToken cancellationToken = default);

    Task<PlanResponseDto?> CreateAsync(CreatePlanRequestDto request, CancellationToken cancellationToken = default);

    Task<PlanResponseDto?> UpdateAsync(Guid planId, UpdatePlanRequestDto request, CancellationToken cancellationToken = default);

    Task<PlanResponseDto?> UpdateStatusAsync(Guid planId, bool isActive, CancellationToken cancellationToken = default);

    Task<GatewayConfigurationResponseDto?> GetGatewayConfigurationAsync(
        Guid planId,
        CancellationToken cancellationToken = default);

    Task<GatewayConfigurationResponseDto?> UpdateGatewayConfigurationAsync(
        Guid planId,
        UpdateGatewayConfigurationRequestDto request,
        CancellationToken cancellationToken = default);
}
