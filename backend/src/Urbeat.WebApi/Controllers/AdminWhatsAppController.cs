using Urbeat.Application.Interfaces;
using Urbeat.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Urbeat.WebApi.Controllers;

/// <summary>
/// Manual, admin-only operational surface for the WhatsApp business profile. It is never invoked
/// from startup or from a webhook request. The profile picture is global for the Phone Number ID.
/// </summary>
[ApiController]
[Route("api/admin/whatsapp")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public sealed class AdminWhatsAppController : ControllerBase
{
    private readonly IWhatsAppBusinessProfileService _profileService;

    public AdminWhatsAppController(IWhatsAppBusinessProfileService profileService)
    {
        _profileService = profileService;
    }

    [HttpPost("profile-picture")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> UpdateProfilePicture(CancellationToken cancellationToken)
    {
        var result = await _profileService.UpdateProfilePictureAsync(cancellationToken);

        if (result.Success)
        {
            return Ok(new { success = true });
        }

        if (string.Equals(result.ErrorCode, "not_configured", StringComparison.Ordinal))
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new { success = false, errorCode = result.ErrorCode });
        }

        return StatusCode(
            StatusCodes.Status502BadGateway,
            new { success = false, errorCode = result.ErrorCode });
    }
}
