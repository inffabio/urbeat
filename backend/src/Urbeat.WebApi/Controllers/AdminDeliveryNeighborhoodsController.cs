using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Urbeat.WebApi.Controllers;

/// <summary>
/// Delivery neighborhoods are global reference data shared by every store in a city. Creating one
/// therefore mutates data outside the caller's store scope. Sellers may only add a neighborhood to
/// their own store's city through <c>POST /api/stores/delivery-neighborhoods</c>; unrestricted
/// creation for any city remains an administrator operation exposed under <c>/api/admin</c>.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public sealed class AdminDeliveryNeighborhoodsController : ControllerBase
{
    private readonly IStoreService _storeService;

    public AdminDeliveryNeighborhoodsController(IStoreService storeService)
    {
        _storeService = storeService;
    }

    [HttpPost("delivery-neighborhoods")]
    [ProducesResponseType<DeliveryNeighborhoodResponseDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateDeliveryNeighborhood([FromBody] CreateDeliveryNeighborhoodRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _storeService.CreateDeliveryNeighborhoodAsync(request.Neighborhood, request.City, cancellationToken);
        if (result is null)
            return Conflict(new { error = "Já existe um bairro com esse nome nesta cidade." });

        return StatusCode(StatusCodes.Status201Created, result);
    }
}
