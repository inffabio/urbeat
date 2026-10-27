using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;

namespace Urbeat.WebApi.Controllers;

/// <summary>
/// Public, tokenized route link used by the motoboy. No authenticated identity is involved:
/// the token is the only credential and invalid/expired/revoked links never expose route data.
/// </summary>
[ApiController]
[Route("api/public/delivery-routes")]
[AllowAnonymous]
[EnableRateLimiting("public-route")]
public sealed class PublicDeliveryRoutesController : ControllerBase
{
    private readonly IDeliveryRouteService _deliveryRouteService;
    private readonly IValidator<ConfirmDeliveryPinRequestDto> _pinValidator;

    public PublicDeliveryRoutesController(
        IDeliveryRouteService deliveryRouteService,
        IValidator<ConfirmDeliveryPinRequestDto> pinValidator)
    {
        _deliveryRouteService = deliveryRouteService;
        _pinValidator = pinValidator;
    }

    [HttpGet("{token}")]
    [ProducesResponseType<PublicDeliveryRouteResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    public async Task<IActionResult> GetRoute([FromRoute] string token, CancellationToken cancellationToken)
    {
        var result = await _deliveryRouteService.GetPublicRouteAsync(token, cancellationToken);
        if (result.NotFound) return NotFound();
        if (result.Gone)
        {
            return StatusCode(
                StatusCodes.Status410Gone,
                new { error = "Este link de rota expirou ou foi revogado." });
        }

        return Ok(result.Route);
    }

    [HttpPost("{token}/stops/{sequence:int}/deliver")]
    [ProducesResponseType<PublicDeliveryRoutePinResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    [ProducesResponseType(StatusCodes.Status423Locked)]
    public async Task<IActionResult> ConfirmDelivery(
        [FromRoute] string token,
        [FromRoute] int sequence,
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

        var result = await _deliveryRouteService.ConfirmPublicDeliveryAsync(
            token,
            sequence,
            request.Pin,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        if (result.NotFound) return NotFound();
        if (result.Gone)
        {
            return StatusCode(
                StatusCodes.Status410Gone,
                new { error = "Este link de rota expirou ou foi revogado." });
        }

        if (result.ConcurrentUpdate && !result.PinLocked)
        {
            return Conflict(new
            {
                error = "Outra confirmação aconteceu ao mesmo tempo. Atualize a rota e confira o próximo cliente.",
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
            return Conflict(new { error = "Este pedido não possui PIN de entrega." });
        }

        if (result.InvalidState)
        {
            return Conflict(new { error = "Esta entrega não é a próxima da rota ou o pedido ainda não saiu para entrega." });
        }

        return Ok(result);
    }
}
