using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Security;

namespace Urbeat.WebApi.Controllers;

/// <summary>
/// Seller-side operational management of motoboys. The motoboy has no account, password or
/// session: the store registers name/phone, assigns orders and shares public route/delivery
/// links. Seller ownership is enforced by the service for every operation.
/// </summary>
[ApiController]
[Route("api/delivery-drivers")]
public sealed class DeliveryDriversController : ControllerBase
{
    private readonly IDeliveryDriverService _deliveryDriverService;
    private readonly IValidator<UpsertStoreDeliveryDriverRequestDto> _upsertValidator;

    public DeliveryDriversController(
        IDeliveryDriverService deliveryDriverService,
        IValidator<UpsertStoreDeliveryDriverRequestDto> upsertValidator)
    {
        _deliveryDriverService = deliveryDriverService;
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
    /// Store-scoped collection kept for clients that already post here. The payload is the same
    /// simple operational registration accepted by <see cref="Create"/>; e-mail/invite fields are
    /// no longer part of the contract because the motoboy has no account.
    /// </summary>
    [HttpPost("store")]
    [Authorize(Policy = AuthorizationPolicies.SellerOnly)]
    [ProducesResponseType<SellerDeliveryDriverResponseDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<IActionResult> CreateForStore(
        [FromBody] CreateStoreDeliveryDriverRequestDto request,
        CancellationToken cancellationToken)
    {
        return Create(new UpsertStoreDeliveryDriverRequestDto
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
