using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Urbeat.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.WebApi.Controllers;

[ApiController]
[Route("api/seller")]
[Authorize(Policy = AuthorizationPolicies.SellerOnly)]
public sealed class SellerController : ControllerBase
{
    [HttpGet("profile")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProfile(
        [FromServices] UserManager<IdentityUser<Guid>> userManager,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var user = await userManager.Users
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null) return NotFound();

        var document = User.FindFirstValue("Document");
        var claims = await userManager.GetClaimsAsync(user);

        return Ok(new
        {
            fullName = claims.FirstOrDefault(c => c.Type == "FullName")?.Value,
            document,
            phoneNumber = user.PhoneNumber,
            email = user.Email
        });
    }

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

    private Guid? GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (claim is not null && Guid.TryParse(claim.Value, out var id))
            return id;
        return null;
    }
}