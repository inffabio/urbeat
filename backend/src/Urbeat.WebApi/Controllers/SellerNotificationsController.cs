using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Urbeat.WebApi.Controllers;

[ApiController]
[Route("api/seller/notifications")]
[Authorize(Policy = AuthorizationPolicies.SellerOnly)]
public sealed class SellerNotificationsController : ControllerBase
{
    private readonly INotificationService _notificationService;

    public SellerNotificationsController(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    [HttpGet]
    [ProducesResponseType<SellerNotificationsResponseDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var sellerUserId = GetCurrentUserId();
        if (sellerUserId is null)
        {
            return Unauthorized();
        }

        var notifications = await _notificationService.ListSellerNotificationsAsync(sellerUserId.Value, cancellationToken);
        return Ok(notifications);
    }

    [HttpPatch("{notificationId}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkAsRead([FromRoute] Guid notificationId, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        var ok = await _notificationService.MarkAsReadAsync(notificationId, userId.Value, cancellationToken);
        return ok ? NoContent() : NotFound();
    }

    private Guid? GetCurrentUserId()
    {
        var subject = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(subject, out var userId) ? userId : null;
    }
}
