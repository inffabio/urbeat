using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Urbeat.Infrastructure.Services;

/// <summary>
/// Adds a short-lived cache to the anonymous public plan offer without changing any write path or
/// DTO. Every mutation invalidates the offer, so admins still observe their changes immediately in
/// the same process; the TTL only bounds staleness when the database is changed out of band.
/// </summary>
public sealed class CachedPlanService : IPlanService
{
    private readonly IPlanService _inner;
    private readonly PublicPlanOfferCache _publicOfferCache;
    private readonly IServiceScopeFactory _scopeFactory;

    public CachedPlanService(
        IPlanService inner,
        PublicPlanOfferCache publicOfferCache,
        IServiceScopeFactory scopeFactory)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _publicOfferCache = publicOfferCache ?? throw new ArgumentNullException(nameof(publicOfferCache));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    public Task<IReadOnlyList<PlanResponseDto>> ListAllAsync(CancellationToken cancellationToken = default)
        => _inner.ListAllAsync(cancellationToken);

    public Task<IReadOnlyList<PlanResponseDto>> ListActiveAsync(CancellationToken cancellationToken = default)
        => _inner.ListActiveAsync(cancellationToken);

    public Task<PublicPlanOfferResponseDto> GetPublicOfferAsync(CancellationToken cancellationToken = default)
        => _publicOfferCache.GetOrCreateAsync(LoadPublicOfferAsync, cancellationToken);

    public async Task<PlanResponseDto?> CreateAsync(
        CreatePlanRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var result = await _inner.CreateAsync(request, cancellationToken).ConfigureAwait(false);
        _publicOfferCache.Invalidate();
        return result;
    }

    public async Task<PlanResponseDto?> UpdateAsync(
        Guid planId,
        UpdatePlanRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var result = await _inner.UpdateAsync(planId, request, cancellationToken).ConfigureAwait(false);
        _publicOfferCache.Invalidate();
        return result;
    }

    public async Task<PlanResponseDto?> UpdateStatusAsync(
        Guid planId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var result = await _inner.UpdateStatusAsync(planId, isActive, cancellationToken).ConfigureAwait(false);
        _publicOfferCache.Invalidate();
        return result;
    }

    /// <summary>
    /// Loads the offer from an isolated scope. The cache may continue a refresh after the request
    /// that triggered it has been aborted, so it must not capture that request's scoped
    /// <c>ApplicationDbContext</c>.
    /// </summary>
    private async Task<PublicPlanOfferResponseDto> LoadPublicOfferAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var planService = scope.ServiceProvider.GetRequiredService<PlanService>();
        return await planService.GetPublicOfferAsync(cancellationToken).ConfigureAwait(false);
    }
}
