using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Infrastructure.Helpers;
using Urbeat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace Urbeat.WebApi.Controllers;

[ApiController]
[Route("api/public/stores")]
[AllowAnonymous]
public sealed class PublicStoresController : ControllerBase
{
    private readonly IStoreReadRepository _storeReadRepository;
    private readonly ApplicationDbContext _dbContext;

    public PublicStoresController(IStoreReadRepository storeReadRepository, ApplicationDbContext dbContext)
    {
        _storeReadRepository = storeReadRepository;
        _dbContext = dbContext;
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyCollection<StorePublicListItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string? cuisineType, CancellationToken cancellationToken)
    {
        var stores = await _storeReadRepository.ListPublicAsync(cuisineType, cancellationToken);
        return Ok(stores);
    }

    [HttpGet("{storeId}")]
    [ProducesResponseType<StorePublicDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] Guid storeId, CancellationToken cancellationToken)
    {
        var store = await _storeReadRepository.GetPublicByIdAsync(storeId, cancellationToken);
        if (store is null)
        {
            return NotFound();
        }

        return Ok(store);
    }

    [HttpGet("by-slug/{slug}")]
    [ProducesResponseType<StorePublicDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBySlug([FromRoute] string slug, CancellationToken cancellationToken)
    {
        var store = await _storeReadRepository.GetPublicBySlugAsync(slug, cancellationToken);
        if (store is null)
        {
            return NotFound();
        }

        return Ok(store);
    }

    [HttpGet("by-path/{storePath}")]
    [ProducesResponseType<StorePublicDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByPath([FromRoute] string storePath, CancellationToken cancellationToken)
    {
        var store = await _storeReadRepository.GetPublicByPathAsync(storePath, cancellationToken);
        if (store is null)
        {
            return NotFound();
        }

        return Ok(store);
    }

    [HttpGet("{storeId}/delivery-check")]
    [ProducesResponseType<DeliveryCheckResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeliveryCheck(
        [FromRoute] Guid storeId,
        [FromQuery] string neighborhood,
        CancellationToken cancellationToken)
    {
        var store = await _dbContext.Stores
            .Include(s => s.DeliveryAreas)
            .FirstOrDefaultAsync(s => s.Id == storeId, cancellationToken);

        if (store is null)
            return NotFound();

        var normalized = NeighborhoodNormalizer.Normalize(neighborhood);
        var area = store.DeliveryAreas
            .FirstOrDefault(a => NeighborhoodNormalizer.Normalize(a.Neighborhood) == normalized);

        return Ok(new DeliveryCheckResponseDto
        {
            Covered = area is not null,
            DeliveryFee = area?.DeliveryFee ?? 0m,
        });
    }
}

/// <summary>Resposta do endpoint público de verificação de cobertura de entrega.</summary>
public sealed class DeliveryCheckResponseDto
{
    public bool Covered { get; init; }
    public decimal DeliveryFee { get; init; }
}
