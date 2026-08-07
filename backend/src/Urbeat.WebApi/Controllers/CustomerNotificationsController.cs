using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Urbeat.WebApi.Controllers;

[ApiController]
[Route("api/customer/notifications")]
[Authorize(Policy = AuthorizationPolicies.CustomerOnly)]
public sealed class CustomerNotificationsController : ControllerBase
{
    private readonly INotificationService _notificationService;

    public CustomerNotificationsController(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    [HttpGet]
    [ProducesResponseType<CustomerNotificationsResponseDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var customerUserId = GetCurrentUserId();
        if (customerUserId is null)
        {
            return Unauthorized();
        }

        var notifications = await _notificationService.ListCustomerNotificationsAsync(customerUserId.Value, cancellationToken);
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
