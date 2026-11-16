using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Urbeat.WebApi.Controllers;

[ApiController]
[Route("api/neighborhoods")]
public class NeighborhoodsController : ControllerBase
{
    private readonly IOsmService _osmService;

    public NeighborhoodsController(IOsmService osmService)
    {
        _osmService = osmService;
    }

    [HttpGet("cities/{cityId:guid}/search")]
    [Authorize(Policy = AuthorizationPolicies.StoreAccess)]
    [ProducesResponseType<IReadOnlyCollection<NeighborhoodSearchResultDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchNeighborhoods(
        [FromRoute] Guid cityId,
        [FromQuery] Guid? storeId,
        [FromQuery] string? search,
        [FromQuery] bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var results = await _osmService.SearchNeighborhoodsAsync(cityId, storeId, search, activeOnly, cancellationToken);
        return Ok(results);
    }

    [HttpGet("cities/{cityId:guid}/map")]
    [Authorize(Policy = AuthorizationPolicies.StoreAccess)]
    [ProducesResponseType<NeighborhoodMapResponseDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMapData(
        [FromRoute] Guid cityId,
        [FromQuery] Guid? storeId,
        CancellationToken cancellationToken)
    {
        var result = await _osmService.GetNeighborhoodsMapAsync(cityId, storeId, cancellationToken);
        return Ok(result);
    }

    [HttpGet("cities")]
    [Authorize(Policy = AuthorizationPolicies.StoreAccess)]
    [ProducesResponseType<IReadOnlyCollection<CityResponseDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCities(CancellationToken cancellationToken)
    {
        var results = await _osmService.GetCitiesAsync(cancellationToken);
        return Ok(results);
    }

    // Global neighborhood import stays seller-only: it is an operational data operation, not a
    // store configuration task exposed in the delegated dashboard.
    [HttpPost("import-by-city")]
    [Authorize(Policy = AuthorizationPolicies.SellerOnly)]
    [ProducesResponseType<ImportNeighborhoodsResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ImportByCity(
        [FromBody] ImportNeighborhoodsByCityRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.City) || string.IsNullOrWhiteSpace(request.Uf))
        {
            return BadRequest();
        }

        var result = await _osmService.ImportNeighborhoodsByCityNameAsync(
            request.City,
            request.Uf,
            request.StoreId,
            cancellationToken);

        return Ok(result);
    }
}
