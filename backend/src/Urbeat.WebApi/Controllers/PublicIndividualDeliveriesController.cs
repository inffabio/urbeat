using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;

namespace Urbeat.WebApi.Controllers;

/// <summary>
/// Public, tokenized individual delivery link used by the motoboy when the store does not create
/// a route. The token is the only credential; invalid/expired links never expose order data and
/// the delivery PIN is never returned by this controller.
/// </summary>
[ApiController]
[Route("api/public/deliveries")]
[AllowAnonymous]
[EnableRateLimiting("public-route")]
public sealed class PublicIndividualDeliveriesController : ControllerBase
{
    private readonly IIndividualDeliveryLinkService _deliveryLinkService;
    private readonly IValidator<ConfirmDeliveryPinRequestDto> _pinValidator;

    public PublicIndividualDeliveriesController(
        IIndividualDeliveryLinkService deliveryLinkService,
        IValidator<ConfirmDeliveryPinRequestDto> pinValidator)
    {
        _deliveryLinkService = deliveryLinkService;
        _pinValidator = pinValidator;
    }

    [HttpGet("{token}")]
    [ProducesResponseType<PublicDeliveryRouteResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    public async Task<IActionResult> GetDelivery([FromRoute] string token, CancellationToken cancellationToken)
    {
        var result = await _deliveryLinkService.GetPublicDeliveryAsync(token, cancellationToken);
        if (result.NotFound) return NotFound();
        if (result.Gone)
        {
            return StatusCode(
                StatusCodes.Status410Gone,
                new { error = "Este link de entrega expirou ou foi revogado." });
        }

        return Ok(result.Route);
    }

    [HttpPost("{token}/deliver")]
    [ProducesResponseType<PublicDeliveryRoutePinResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    [ProducesResponseType(StatusCodes.Status423Locked)]
    public async Task<IActionResult> ConfirmDelivery(
        [FromRoute] string token,
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

        var result = await _deliveryLinkService.ConfirmPublicDeliveryAsync(
            token,
            request.Pin,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        if (result.NotFound) return NotFound();
        if (result.Gone)
        {
            return StatusCode(
                StatusCodes.Status410Gone,
                new { error = "Este link de entrega expirou ou foi revogado." });
        }

        if (result.ConcurrentUpdate && !result.PinLocked)
        {
            return Conflict(new
            {
                error = "Outra confirmação aconteceu ao mesmo tempo. Atualize a entrega antes de tentar novamente.",
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
            return Conflict(new { error = "O pedido ainda não saiu para entrega ou a entrega já foi encerrada." });
        }

        return Ok(result);
    }
}
