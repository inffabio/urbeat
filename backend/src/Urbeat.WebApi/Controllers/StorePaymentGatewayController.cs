using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentValidation;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Security;
using Urbeat.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Urbeat.WebApi.Controllers;

[ApiController]
[Route("api/stores/{storeId}/payment-gateway")]
[Authorize(Policy = AuthorizationPolicies.SellerOnly)]
public sealed class StorePaymentGatewayController : ControllerBase
{
    private readonly IStorePaymentGatewayConfigService _configService;
    private readonly IValidator<UpsertPaymentGatewayConfigRequestDto> _validator;

    public StorePaymentGatewayController(
        IStorePaymentGatewayConfigService configService,
        IValidator<UpsertPaymentGatewayConfigRequestDto> validator)
    {
        _configService = configService;
        _validator = validator;
    }

    [HttpGet]
    [ProducesResponseType<PaymentGatewayConfigResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        [FromRoute] Guid storeId,
        [FromQuery] PaymentGateway gateway = PaymentGateway.MercadoPago,
        CancellationToken cancellationToken = default)
    {
        var ownerUserId = GetCurrentUserId();
        if (ownerUserId is null)
            return Unauthorized();

        var config = await _configService.GetByStoreAsync(ownerUserId.Value, storeId, gateway, cancellationToken);
        if (config is null)
            return NotFound();

        return Ok(config);
    }

    [HttpPut]
    [ProducesResponseType<PaymentGatewayConfigResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Upsert(
        [FromRoute] Guid storeId,
        [FromBody] UpsertPaymentGatewayConfigRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var validation = await _validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validation.Errors
                .GroupBy(x => x.PropertyName)
                .ToDictionary(
                    keySelector: group => group.Key,
                    elementSelector: group => group.Select(x => x.ErrorMessage).ToArray())));
        }

        var ownerUserId = GetCurrentUserId();
        if (ownerUserId is null)
            return Unauthorized();

        var result = await _configService.UpsertAsync(
            ownerUserId.Value, storeId, request,
            HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        if (result.NotFound)
            return NotFound();

        if (result.Forbidden)
            return Forbid();

        return Ok(result.Config);
    }

    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        [FromRoute] Guid storeId,
        [FromQuery] PaymentGateway gateway = PaymentGateway.MercadoPago,
        CancellationToken cancellationToken = default)
    {
        var ownerUserId = GetCurrentUserId();
        if (ownerUserId is null)
            return Unauthorized();

        var deleted = await _configService.DeleteAsync(
            ownerUserId.Value, storeId, gateway,
            HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        if (!deleted)
            return NotFound();

        return NoContent();
    }

    private Guid? GetCurrentUserId()
    {
        var subject = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(subject, out var userId) ? userId : null;
    }
}
