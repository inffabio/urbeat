using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Urbeat.Application.Security;
using Urbeat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.WebApi.Controllers;

[ApiController]
[Route("api/customer")]
[Authorize(Policy = AuthorizationPolicies.CustomerOnly)]
public sealed class CustomerController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public CustomerController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("home")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult GetHome()
    {
        return Ok(new
        {
            area = "customer",
            message = "Customer authorized."
        });
    }

    [HttpGet("me")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetCurrentCustomer(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var profile = await _dbContext.Users
            .Where(user => user.Id == userId.Value)
            .Select(user => new
            {
                FullName = _dbContext.UserClaims
                    .Where(claim => claim.UserId == user.Id && claim.ClaimType == "FullName")
                    .Select(claim => claim.ClaimValue)
                    .FirstOrDefault() ?? user.Email ?? string.Empty,
                Email = user.Email ?? string.Empty,
                PhoneNumber = user.PhoneNumber,
                PrimaryAddressId = _dbContext.CustomerAddresses
                    .Where(address => address.UserId == user.Id && address.IsPrimary)
                    .OrderByDescending(address => address.CreatedAtUtc)
                    .Select(address => (Guid?)address.Id)
                    .FirstOrDefault()
            })
            .SingleOrDefaultAsync(cancellationToken);

        return profile is null ? Unauthorized() : Ok(profile);
    }

    private Guid? GetCurrentUserId()
    {
        var subject = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(subject, out var userId) ? userId : null;
    }
}
