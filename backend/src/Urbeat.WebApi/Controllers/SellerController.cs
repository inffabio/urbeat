using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Urbeat.Application.DTOs;
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

    [HttpPut("profile")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdateProfile(
        [FromBody] UpdateSellerProfileRequestDto request,
        [FromServices] UserManager<IdentityUser<Guid>> userManager,
        [FromServices] IValidator<UpdateSellerProfileRequestDto> validator,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validation.Errors
                .GroupBy(error => error.PropertyName)
                .ToDictionary(group => group.Key, group => group.Select(error => error.ErrorMessage).ToArray())));
        }

        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var user = await userManager.FindByIdAsync(userId.Value.ToString());
        if (user is null) return Unauthorized();

        var fullName = request.FullName.Trim();
        var existingClaim = (await userManager.GetClaimsAsync(user))
            .FirstOrDefault(claim => claim.Type == "FullName");

        IdentityResult result;
        if (existingClaim is null)
        {
            result = await userManager.AddClaimAsync(user, new Claim("FullName", fullName));
        }
        else if (string.Equals(existingClaim.Value, fullName, StringComparison.Ordinal))
        {
            result = IdentityResult.Success;
        }
        else
        {
            result = await userManager.ReplaceClaimAsync(user, existingClaim, new Claim("FullName", fullName));
        }

        if (!result.Succeeded)
        {
            return ValidationProblem(new ValidationProblemDetails(
                new Dictionary<string, string[]>
                {
                    ["fullName"] = result.Errors
                        .Select(error => error.Description)
                        .Where(description => !string.IsNullOrWhiteSpace(description))
                        .DefaultIfEmpty("Não foi possível atualizar o nome do contratante.")
                        .ToArray()
                }));
        }

        return await GetProfile(userManager, cancellationToken);
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