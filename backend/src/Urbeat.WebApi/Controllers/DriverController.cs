using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Security;

namespace Urbeat.WebApi.Controllers;

[ApiController]
[Route("api/driver")]
[Authorize(Policy = AuthorizationPolicies.DeliveryDriverOnly)]
public sealed class DriverController : ControllerBase
{
    private readonly IDeliveryDriverService _deliveryDriverService;
    private readonly IValidator<ConfirmDeliveryPinRequestDto> _pinValidator;

    public DriverController(
        IDeliveryDriverService deliveryDriverService,
        IValidator<ConfirmDeliveryPinRequestDto> pinValidator)
    {
        _deliveryDriverService = deliveryDriverService;
        _pinValidator = pinValidator;
    }

    [HttpGet("stores")]
    [ProducesResponseType<IReadOnlyCollection<DeliveryDriverStoreResponseDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Stores(CancellationToken cancellationToken)
    {
        var driverUserId = GetCurrentUserId();
        if (driverUserId is null) return Unauthorized();

        var stores = await _deliveryDriverService.ListDriverStoresAsync(driverUserId.Value, cancellationToken);
        return Ok(stores);
    }

    [HttpGet("orders")]
    [ProducesResponseType<DriverOrdersResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Orders([FromQuery] Guid storeId, CancellationToken cancellationToken)
    {
        var driverUserId = GetCurrentUserId();
        if (driverUserId is null) return Unauthorized();

        var result = await _deliveryDriverService.ListDriverOrdersAsync(
            driverUserId.Value,
            storeId,
            cancellationToken);

        if (result.StoreNotAvailable) return Forbid();

        return Ok(result);
    }

    [HttpGet("orders/{orderId:guid}")]
    [ProducesResponseType<DriverOrderSummaryResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Order(Guid orderId, CancellationToken cancellationToken)
    {
        var driverUserId = GetCurrentUserId();
        if (driverUserId is null) return Unauthorized();

        var order = await _deliveryDriverService.GetDriverOrderAsync(
            driverUserId.Value,
            orderId,
            cancellationToken);

        return order is null ? NotFound() : Ok(order);
    }

    [HttpPost("orders/{orderId:guid}/start")]
    [ProducesResponseType<DeliveryDriverActionResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> StartDelivery(Guid orderId, CancellationToken cancellationToken)
    {
        var driverUserId = GetCurrentUserId();
        if (driverUserId is null) return Unauthorized();

        var result = await _deliveryDriverService.StartDeliveryAsync(
            driverUserId.Value,
            orderId,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        return ToActionResult(result, "A entrega não pode ser iniciada neste estado.");
    }

    [HttpPost("orders/{orderId:guid}/confirm-pin")]
    [ProducesResponseType<DeliveryDriverActionResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status423Locked)]
    public async Task<IActionResult> ConfirmPin(
        Guid orderId,
        [FromBody] ConfirmDeliveryPinRequestDto request,
        CancellationToken cancellationToken)
    {
        var validation = await _pinValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validation.Errors
                .GroupBy(x => x.PropertyName)
                .ToDictionary(
                    keySelector: group => group.Key,
                    elementSelector: group => group.Select(x => x.ErrorMessage).ToArray())));
        }

        var driverUserId = GetCurrentUserId();
        if (driverUserId is null) return Unauthorized();

        var result = await _deliveryDriverService.ConfirmDeliveryPinAsync(
            driverUserId.Value,
            orderId,
            request.Pin,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        if (result.ConcurrentUpdate && !result.PinLocked)
        {
            return Conflict(new
            {
                error = "Outra tentativa de PIN foi registrada ao mesmo tempo. Atualize o pedido e tente novamente.",
                remainingAttempts = result.RemainingAttempts
            });
        }

        if (result.PinLocked)
        {
            return StatusCode(
                StatusCodes.Status423Locked,
                new { error = "Limite de tentativas do PIN atingido. Peça ajuda à loja.", remainingAttempts = 0 });
        }

        if (result.PinInvalid)
        {
            return BadRequest(new
            {
                error = $"PIN incorreto. Tentativas restantes: {result.RemainingAttempts}.",
                remainingAttempts = result.RemainingAttempts
            });
        }

        if (result.PinUnavailable)
        {
            return Conflict(new { error = "Este pedido não possui PIN de entrega. Use a confirmação do cliente." });
        }

        return ToActionResult(result, "O PIN não pode ser confirmado neste estado.");
    }

    private IActionResult ToActionResult(DeliveryDriverActionResultDto result, string invalidStateMessage)
    {
        if (result.NotFound) return NotFound();
        if (result.Forbidden) return Forbid();
        if (result.InvalidState) return Conflict(new { error = invalidStateMessage });

        return Ok(result);
    }

    private Guid? GetCurrentUserId()
    {
        var subject = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(subject, out var userId) ? userId : null;
    }
}
