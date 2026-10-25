using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Security;

namespace Urbeat.WebApi.Controllers;

[ApiController]
[Route("api/delivery-drivers")]
public sealed class DeliveryDriversController : ControllerBase
{
    private readonly IDeliveryDriverService _deliveryDriverService;
    private readonly IValidator<InviteDeliveryDriverRequestDto> _inviteValidator;
    private readonly IValidator<AcceptDeliveryDriverInviteRequestDto> _acceptValidator;

    public DeliveryDriversController(
        IDeliveryDriverService deliveryDriverService,
        IValidator<InviteDeliveryDriverRequestDto> inviteValidator,
        IValidator<AcceptDeliveryDriverInviteRequestDto> acceptValidator)
    {
        _deliveryDriverService = deliveryDriverService;
        _inviteValidator = inviteValidator;
        _acceptValidator = acceptValidator;
    }

    [HttpGet("store")]
    [Authorize(Policy = AuthorizationPolicies.SellerOnly)]
    [ProducesResponseType<IReadOnlyCollection<SellerDeliveryDriverResponseDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListStoreDrivers(CancellationToken cancellationToken)
    {
        var sellerUserId = GetCurrentUserId();
        if (sellerUserId is null) return Unauthorized();

        var drivers = await _deliveryDriverService.ListStoreDriversAsync(sellerUserId.Value, cancellationToken);
        return Ok(drivers);
    }

    [HttpPost("store")]
    [Authorize(Policy = AuthorizationPolicies.SellerOnly)]
    [ProducesResponseType<SellerDeliveryDriverResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Invite(
        [FromBody] InviteDeliveryDriverRequestDto request,
        CancellationToken cancellationToken)
    {
        var validation = await _inviteValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validation.Errors
                .GroupBy(x => x.PropertyName)
                .ToDictionary(
                    keySelector: group => group.Key,
                    elementSelector: group => group.Select(x => x.ErrorMessage).ToArray())));
        }

        var sellerUserId = GetCurrentUserId();
        if (sellerUserId is null) return Unauthorized();

        var result = await _deliveryDriverService.InviteAsync(
            sellerUserId.Value,
            request,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        if (result.Forbidden) return Forbid();
        if (result.Conflict)
        {
            return Conflict(new { error = result.Error });
        }

        return Ok(result.Driver);
    }

    [HttpPost("store/{storeDriverId:guid}/resend-invite")]
    [Authorize(Policy = AuthorizationPolicies.SellerOnly)]
    [ProducesResponseType<SellerDeliveryDriverResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ResendInvite(Guid storeDriverId, CancellationToken cancellationToken)
    {
        var sellerUserId = GetCurrentUserId();
        if (sellerUserId is null) return Unauthorized();

        var result = await _deliveryDriverService.ResendInviteAsync(
            sellerUserId.Value,
            storeDriverId,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        if (result.NotFound) return NotFound();
        if (result.Forbidden) return Forbid();
        if (result.Conflict) return Conflict(new { error = result.Error });

        return Ok(result.Driver);
    }

    [HttpPost("store/{storeDriverId:guid}/revoke")]
    [Authorize(Policy = AuthorizationPolicies.SellerOnly)]
    [ProducesResponseType<SellerDeliveryDriverResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Revoke(Guid storeDriverId, CancellationToken cancellationToken)
    {
        var sellerUserId = GetCurrentUserId();
        if (sellerUserId is null) return Unauthorized();

        var result = await _deliveryDriverService.RevokeAsync(
            sellerUserId.Value,
            storeDriverId,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        if (result.NotFound) return NotFound();
        if (result.Forbidden) return Forbid();
        if (result.Conflict) return Conflict(new { error = result.Error });

        return Ok(result.Driver);
    }

    [HttpGet("invites/{token}")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType<DeliveryDriverInvitePreviewResponseDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PreviewInvite([FromRoute] string token, CancellationToken cancellationToken)
    {
        var preview = await _deliveryDriverService.GetInvitePreviewAsync(token, cancellationToken);
        return Ok(preview);
    }

    [HttpPost("invites/{token}/accept")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType<AcceptDeliveryDriverInviteResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AcceptInvite(
        [FromRoute] string token,
        [FromBody] AcceptDeliveryDriverInviteRequestDto request,
        CancellationToken cancellationToken)
    {
        var validation = await _acceptValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validation.Errors
                .GroupBy(x => x.PropertyName)
                .ToDictionary(
                    keySelector: group => group.Key,
                    elementSelector: group => group.Select(x => x.ErrorMessage).ToArray())));
        }

        var result = await _deliveryDriverService.AcceptInviteAsync(
            token,
            request,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        if (!result.Succeeded)
        {
            return BadRequest(new { error = result.Error ?? "Não foi possível aceitar o convite." });
        }

        return Ok(result);
    }

    private Guid? GetCurrentUserId()
    {
        var subject = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(subject, out var userId) ? userId : null;
    }
}
