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
[Route("api/delivery-routes")]
public sealed class DeliveryRoutesController : ControllerBase
{
    private readonly IDeliveryRouteService _deliveryRouteService;
    private readonly IValidator<CreateDeliveryRouteRequestDto> _createValidator;
    private readonly IValidator<UpdateDeliveryRouteRequestDto> _updateValidator;
    private readonly IValidator<SendDeliveryRoutesRequestDto> _sendValidator;

    public DeliveryRoutesController(
        IDeliveryRouteService deliveryRouteService,
        IValidator<CreateDeliveryRouteRequestDto> createValidator,
        IValidator<UpdateDeliveryRouteRequestDto> updateValidator,
        IValidator<SendDeliveryRoutesRequestDto> sendValidator)
    {
        _deliveryRouteService = deliveryRouteService;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _sendValidator = sendValidator;
    }

    [HttpGet("store")]
    [Authorize(Policy = AuthorizationPolicies.SellerOnly)]
    [ProducesResponseType<IReadOnlyCollection<DeliveryRouteResponseDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListStoreRoutes(CancellationToken cancellationToken)
    {
        var sellerUserId = GetCurrentUserId();
        if (sellerUserId is null) return Unauthorized();

        return Ok(await _deliveryRouteService.ListStoreRoutesAsync(sellerUserId.Value, cancellationToken));
    }

    [HttpGet("store/{routeId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.SellerOnly)]
    [ProducesResponseType<DeliveryRouteResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStoreRoute(Guid routeId, CancellationToken cancellationToken)
    {
        var sellerUserId = GetCurrentUserId();
        if (sellerUserId is null) return Unauthorized();

        var result = await _deliveryRouteService.GetStoreRouteAsync(
            sellerUserId.Value,
            routeId,
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("store")]
    [Authorize(Policy = AuthorizationPolicies.SellerOnly)]
    [ProducesResponseType<DeliveryRouteOperationResultDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateDeliveryRouteRequestDto request,
        CancellationToken cancellationToken)
    {
        var validation = await _createValidator.ValidateAsync(request, cancellationToken);
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

        var result = await _deliveryRouteService.CreateAsync(
            sellerUserId.Value,
            request,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        if (result.Route is not null)
        {
            return StatusCode(StatusCodes.Status201Created, result);
        }

        return ToActionResult(result);
    }

    /// <summary>
    /// Sends every selected delivery to the respective motoboy. The server groups the orders by
    /// assignment, creates or reuses one route per motoboy and returns one absolute link and
    /// WhatsApp message per group. The seller still has to confirm each WhatsApp message.
    /// </summary>
    [HttpPost("store/send")]
    [Authorize(Policy = AuthorizationPolicies.SellerOnly)]
    [ProducesResponseType<SendDeliveryRoutesResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SendBatches(
        [FromBody] SendDeliveryRoutesRequestDto request,
        CancellationToken cancellationToken)
    {
        var validation = await _sendValidator.ValidateAsync(request, cancellationToken);
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

        var result = await _deliveryRouteService.SendBatchesAsync(
            sellerUserId.Value,
            request,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        if (result.Forbidden) return Forbid();
        if (result.PhysicalConfirmationRequired)
        {
            return BadRequest(new
            {
                error = result.Error ?? "Confirme fisicamente os pedidos antes de preparar os links de entrega."
            });
        }
        if (result.OrderNotEligible)
        {
            return BadRequest(new { error = result.Error ?? "A seleção contém pedidos não entregáveis." });
        }
        if (result.DriverNotAvailable)
        {
            return BadRequest(new { error = result.Error ?? "Motoboy indisponível ou inativo." });
        }
        if (result.Conflict)
        {
            return Conflict(new { error = result.Error ?? "A seleção conflita com uma rota ativa." });
        }

        return Ok(result);
    }

    [HttpPut("store/{routeId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.SellerOnly)]
    [ProducesResponseType<DeliveryRouteOperationResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid routeId,
        [FromBody] UpdateDeliveryRouteRequestDto request,
        CancellationToken cancellationToken)
    {
        var validation = await _updateValidator.ValidateAsync(request, cancellationToken);
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

        var result = await _deliveryRouteService.UpdateAsync(
            sellerUserId.Value,
            routeId,
            request,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("store/{routeId:guid}/share")]
    [Authorize(Policy = AuthorizationPolicies.SellerOnly)]
    [ProducesResponseType<DeliveryRouteOperationResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Share(Guid routeId, CancellationToken cancellationToken)
    {
        var sellerUserId = GetCurrentUserId();
        if (sellerUserId is null) return Unauthorized();

        var result = await _deliveryRouteService.ShareAsync(
            sellerUserId.Value,
            routeId,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("store/{routeId:guid}/revoke")]
    [Authorize(Policy = AuthorizationPolicies.SellerOnly)]
    [ProducesResponseType<DeliveryRouteOperationResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Revoke(Guid routeId, CancellationToken cancellationToken)
    {
        var sellerUserId = GetCurrentUserId();
        if (sellerUserId is null) return Unauthorized();

        var result = await _deliveryRouteService.RevokeAsync(
            sellerUserId.Value,
            routeId,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);
        return ToActionResult(result);
    }

    private IActionResult ToActionResult(DeliveryRouteOperationResultDto result)
    {
        if (result.NotFound) return NotFound();
        if (result.Forbidden) return Forbid();
        if (result.DriverNotAvailable)
        {
            return BadRequest(new { error = "Motoboy indisponível, inativo ou vinculado a outra loja." });
        }
        if (result.OrderNotEligible)
        {
            return BadRequest(new { error = result.Error ?? "A rota contém pedidos não entregáveis." });
        }
        if (result.InvalidState) return Conflict(new { error = result.Error });
        if (result.Conflict) return Conflict(new { error = result.Error });

        return Ok(result);
    }

    private Guid? GetCurrentUserId()
    {
        var subject = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(subject, out var userId) ? userId : null;
    }
}
