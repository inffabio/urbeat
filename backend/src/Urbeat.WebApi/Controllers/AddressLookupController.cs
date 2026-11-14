using Urbeat.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Urbeat.WebApi.Controllers;

[ApiController]
[Route("api/address-lookup")]
[AllowAnonymous]
public sealed class AddressLookupController : ControllerBase
{
    private const int MaxStreetSearchLimit = 50;

    private readonly IAddressLookupService _addressLookupService;

    public AddressLookupController(IAddressLookupService addressLookupService)
    {
        _addressLookupService = addressLookupService;
    }

    [HttpGet("cep/{cep}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> LookupByCep([FromRoute] string cep, CancellationToken cancellationToken)
    {
        var digits = new string(cep.Where(char.IsDigit).ToArray());
        if (digits.Length != 8)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid CEP",
                detail: "CEP must contain exactly 8 digits.",
                instance: HttpContext.Request.Path);
        }

        var address = await _addressLookupService.LookupByCepAsync(digits, cancellationToken);
        if (address is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "CEP Not Found",
                detail: $"No address found for CEP {digits}.",
                instance: HttpContext.Request.Path);
        }

        return Ok(address);
    }

    /// <summary>
    /// Prefix search inside one municipality (or one UF). Free-text national scans are rejected.
    /// </summary>
    [HttpGet("streets")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SearchStreets(
        [FromQuery] Guid? cityId,
        [FromQuery] string? uf,
        [FromQuery] string? q,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (cityId is null && string.IsNullOrWhiteSpace(uf))
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Scope required",
                detail: "Informe cityId ou uf para buscar logradouros.",
                instance: HttpContext.Request.Path);
        }

        var normalizedLimit = Math.Clamp(limit, 1, MaxStreetSearchLimit);
        var streets = await _addressLookupService.SearchStreetsAsync(
            cityId,
            uf,
            q,
            normalizedLimit,
            cancellationToken);

        return Ok(streets);
    }
}
