using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentValidation;
using Urbeat.Application.DTOs;
using Urbeat.Application.Payments;
using Urbeat.Application.Security;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Urbeat.WebApi.Controllers;

[ApiController]
[Route("api/payments")]
public sealed class PaymentsController : ControllerBase
{
    private readonly ISender _sender;
    private readonly IValidator<CreateOrderPaymentRequestDto> _createValidator;

    public PaymentsController(
        ISender sender,
        IValidator<CreateOrderPaymentRequestDto> createValidator)
    {
        _sender = sender;
        _createValidator = createValidator;
    }

    [HttpPost("order")]
    [Authorize(Policy = AuthorizationPolicies.CustomerOnly)]
    [ProducesResponseType<OrderPaymentResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateOrderPayment([FromBody] CreateOrderPaymentRequestDto request, CancellationToken cancellationToken)
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

        var customerUserId = GetCurrentUserId();
        if (customerUserId is null)
        {
            return Unauthorized();
        }

        var result = await _sender.Send(new CreateOrderPaymentCommand(
            customerUserId.Value,
            request,
            HttpContext.Connection.RemoteIpAddress?.ToString()), cancellationToken);

        if (result.NotFound)
        {
            return NotFound();
        }

        if (result.UnsupportedMethod)
        {
            return BadRequest(new { error = "Payment method does not require online checkout." });
        }

        if (result.InvalidOrderState)
        {
            BusinessMetrics.PaymentFailures.Inc();
            return Conflict(new { error = "Order is not in pending payment state." });
        }

        return Ok(result.Payment);
    }

    [HttpGet("order/{orderId}")]
    [Authorize(Policy = AuthorizationPolicies.CustomerOnly)]
    [ProducesResponseType<OrderPaymentResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrderPayment([FromRoute] Guid orderId, CancellationToken cancellationToken)
    {
        var customerUserId = GetCurrentUserId();
        if (customerUserId is null)
        {
            return Unauthorized();
        }

        var payment = await _sender.Send(new GetOrderPaymentQuery(customerUserId.Value, orderId), cancellationToken);
        if (payment is null)
        {
            return NotFound();
        }

        return Ok(payment);
    }

    [HttpGet("order/{orderId}/history")]
    [Authorize(Policy = AuthorizationPolicies.CustomerOnly)]
    [ProducesResponseType<IReadOnlyCollection<PaymentStatusHistoryResponseDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOrderPaymentHistory([FromRoute] Guid orderId, CancellationToken cancellationToken)
    {
        var customerUserId = GetCurrentUserId();
        if (customerUserId is null)
        {
            return Unauthorized();
        }

        var history = await _sender.Send(new GetOrderPaymentHistoryQuery(customerUserId.Value, orderId), cancellationToken);
        return Ok(history);
    }

    private Guid? GetCurrentUserId()
    {
        var subject = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(subject, out var userId) ? userId : null;
    }
}
