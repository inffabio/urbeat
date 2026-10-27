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
    private readonly IValidator<UpsertStoreDeliveryDriverRequestDto> _upsertValidator;

    public DeliveryDriversController(
        IDeliveryDriverService deliveryDriverService,
        IValidator<InviteDeliveryDriverRequestDto> inviteValidator,
        IValidator<AcceptDeliveryDriverInviteRequestDto> acceptValidator,
        IValidator<UpsertStoreDeliveryDriverRequestDto> upsertValidator)
    {
        _deliveryDriverService = deliveryDriverService;
        _inviteValidator = inviteValidator;
        _acceptValidator = acceptValidator;
        _upsertValidator = upsertValidator;
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

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.SellerOnly)]
    [ProducesResponseType<SellerDeliveryDriverResponseDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create([FromBody] UpsertStoreDeliveryDriverRequestDto request, CancellationToken cancellationToken)
    {
        var validation = await ValidateUpsertAsync(request, cancellationToken);
        if (validation is not null) return validation;

        var sellerUserId = GetCurrentUserId();
        if (sellerUserId is null) return Unauthorized();

        var result = await _deliveryDriverService.CreateStoreDriverAsync(
            sellerUserId.Value,
            request,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        if (result.Forbidden) return Forbid();

        return StatusCode(StatusCodes.Status201Created, result.Driver);
    }

    /// <summary>
    /// Store-scoped collection. New clients create a simple motoboy here; the same path still
    /// accepts the legacy invite payload (FullName/Email) for already integrated clients.
    /// </summary>
    [HttpPost("store")]
    [Authorize(Policy = AuthorizationPolicies.SellerOnly)]
    [ProducesResponseType<SellerDeliveryDriverResponseDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateForStore(
        [FromBody] CreateStoreDeliveryDriverRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var inviteRequest = new InviteDeliveryDriverRequestDto
            {
                FullName = request.FullName ?? request.Name ?? string.Empty,
                Email = request.Email,
                PhoneNumber = request.PhoneNumber ?? string.Empty
            };

            var validation = await _inviteValidator.ValidateAsync(inviteRequest, cancellationToken);
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
                inviteRequest,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                cancellationToken);

            if (result.Forbidden) return Forbid();
            if (result.Conflict) return Conflict(new { error = result.Error });
            return Ok(result.Driver);
        }

        return await Create(new UpsertStoreDeliveryDriverRequestDto
        {
            Name = request.Name ?? request.FullName ?? string.Empty,
            PhoneNumber = request.PhoneNumber ?? string.Empty,
            WhatsAppPhoneNumber = request.WhatsAppPhoneNumber,
            IsActive = request.IsActive
        }, cancellationToken);
    }

    [HttpPut("{storeDriverId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.SellerOnly)]
    [ProducesResponseType<SellerDeliveryDriverResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid storeDriverId,
        [FromBody] UpsertStoreDeliveryDriverRequestDto request,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateUpsertAsync(request, cancellationToken);
        if (validation is not null) return validation;

        var sellerUserId = GetCurrentUserId();
        if (sellerUserId is null) return Unauthorized();

        var result = await _deliveryDriverService.UpdateStoreDriverAsync(
            sellerUserId.Value,
            storeDriverId,
            request,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        return ToDriverResult(result);
    }

    [HttpPatch("{storeDriverId:guid}/status")]
    [Authorize(Policy = AuthorizationPolicies.SellerOnly)]
    [ProducesResponseType<SellerDeliveryDriverResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetStatus(
        Guid storeDriverId,
        [FromBody] SetStoreDeliveryDriverStatusRequestDto request,
        CancellationToken cancellationToken)
    {
        var sellerUserId = GetCurrentUserId();
        if (sellerUserId is null) return Unauthorized();

        var result = await _deliveryDriverService.SetStoreDriverActiveAsync(
            sellerUserId.Value,
            storeDriverId,
            request.IsActive,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        return ToDriverResult(result);
    }

    [HttpDelete("{storeDriverId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.SellerOnly)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid storeDriverId, CancellationToken cancellationToken)
    {
        var sellerUserId = GetCurrentUserId();
        if (sellerUserId is null) return Unauthorized();

        var result = await _deliveryDriverService.DeleteStoreDriverAsync(
            sellerUserId.Value,
            storeDriverId,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        if (result.NotFound) return NotFound();
        if (result.Forbidden) return Forbid();
        if (result.Conflict) return Conflict(new { error = result.Error });

        return NoContent();
    }

    /// <summary>Legacy invite endpoint kept for links created before the simple registration flow.</summary>
    [HttpPost("store/invite")]
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

    private async Task<IActionResult?> ValidateUpsertAsync(
        UpsertStoreDeliveryDriverRequestDto request,
        CancellationToken cancellationToken)
    {
        var validation = await _upsertValidator.ValidateAsync(request, cancellationToken);
        if (validation.IsValid) return null;

        return ValidationProblem(new ValidationProblemDetails(validation.Errors
            .GroupBy(x => x.PropertyName)
            .ToDictionary(
                keySelector: group => group.Key,
                elementSelector: group => group.Select(x => x.ErrorMessage).ToArray())));
    }

    private IActionResult ToDriverResult(SellerDeliveryDriverOperationResultDto result)
    {
        if (result.NotFound) return NotFound();
        if (result.Forbidden) return Forbid();
        if (result.Conflict) return Conflict(new { error = result.Error });

        return Ok(result.Driver);
    }

    private Guid? GetCurrentUserId()
    {
        var subject = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(subject, out var userId) ? userId : null;
    }
}
