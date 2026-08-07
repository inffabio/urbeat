using Urbeat.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Urbeat.WebApi.Controllers;

[ApiController]
[Route("api/seller")]
[Authorize(Policy = AuthorizationPolicies.SellerOnly)]
public sealed class SellerController : ControllerBase
{
    [HttpGet("panel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult GetPanel()
    {
        return Ok(new
        {
            area = "seller",
            message = "Seller authorized."
        });
    }
}