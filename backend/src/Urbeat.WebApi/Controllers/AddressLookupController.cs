using Urbeat.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Urbeat.WebApi.Controllers;

[ApiController]
[Route("api/address-lookup")]
[AllowAnonymous]
public sealed class AddressLookupController : ControllerBase
{
    private readonly IViaCepService _viaCepService;

    public AddressLookupController(IViaCepService viaCepService)
    {
        _viaCepService = viaCepService;
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

        var address = await _viaCepService.LookupAsync(digits, cancellationToken);
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
}
