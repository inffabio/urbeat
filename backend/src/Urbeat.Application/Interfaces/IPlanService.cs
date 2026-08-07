using Urbeat.Application.DTOs;

namespace Urbeat.Application.Interfaces;

public interface IPlanService
{
    Task<IReadOnlyList<PlanResponseDto>> ListAllAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlanResponseDto>> ListActiveAsync(CancellationToken cancellationToken = default);

    Task<PlanResponseDto?> CreateAsync(CreatePlanRequestDto request, CancellationToken cancellationToken = default);

    Task<PlanResponseDto?> UpdateAsync(Guid planId, UpdatePlanRequestDto request, CancellationToken cancellationToken = default);

    Task<PlanResponseDto?> UpdateStatusAsync(Guid planId, bool isActive, CancellationToken cancellationToken = default);
}