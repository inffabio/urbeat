using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentValidation;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Urbeat.WebApi.Controllers;

[ApiController]
[Route("api/checkout")]
public sealed class CheckoutController : ControllerBase
{
    private readonly ICheckoutService _checkoutService;
    private readonly ICustomerOtpService _customerOtpService;
    private readonly IValidator<CheckoutRequestDto> _validator;

    public CheckoutController(
        ICheckoutService checkoutService,
        IValidator<CheckoutRequestDto> validator,
        ICustomerOtpService customerOtpService)
    {
        _checkoutService = checkoutService;
        _validator = validator;
        _customerOtpService = customerOtpService;
    }

    [HttpPost("customer-verification/start")]
    [AllowAnonymous]
    [ProducesResponseType<StartCustomerVerificationResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> StartCustomerVerification([FromBody] StartCustomerVerificationRequestDto request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _customerOtpService.StartAsync(request, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("customer-session")]
    [AllowAnonymous]
    [ProducesResponseType<ConfirmCustomerVerificationResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateCustomerSession([FromBody] StartCustomerVerificationRequestDto request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _customerOtpService.CreateCustomerSessionAsync(request, cancellationToken);
            if (!result.Succeeded)
            {
                return BadRequest(result);
            }

            if (!string.IsNullOrWhiteSpace(result.RefreshToken) && result.RefreshTokenExpiresAtUtc is not null)
            {
                SetRefreshCookie(result.RefreshToken, result.RefreshTokenExpiresAtUtc.Value);
            }

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("customer-verification/confirm")]
    [AllowAnonymous]
    [ProducesResponseType<ConfirmCustomerVerificationResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ConfirmCustomerVerification([FromBody] ConfirmCustomerVerificationRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _customerOtpService.ConfirmAsync(request, cancellationToken);
        if (!result.Succeeded)
        {
            return BadRequest(result);
        }

        if (!string.IsNullOrWhiteSpace(result.RefreshToken) && result.RefreshTokenExpiresAtUtc is not null)
        {
            SetRefreshCookie(result.RefreshToken, result.RefreshTokenExpiresAtUtc.Value);
        }

        return Ok(result);
    }

    [HttpPost("customer-verification/resend")]
    [AllowAnonymous]
    [ProducesResponseType<ResendCustomerVerificationResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResendCustomerVerification([FromBody] ResendCustomerVerificationRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _customerOtpService.ResendAsync(request, cancellationToken);
        return result.Succeeded ? Ok(result) : BadRequest(result);
    }

    [HttpPost("preview")]
    [AllowAnonymous]
    [ProducesResponseType<CheckoutSummaryResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Preview([FromBody] CheckoutRequestDto request, CancellationToken cancellationToken)
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

        var result = await _checkoutService.PreviewAsync(GetCurrentUserId(), request, cancellationToken);
        if (result.Summary is not null)
        {
            return Ok(result.Summary);
        }

        return ToActionResult(result);
    }

    [HttpPost("confirm")]
    [Authorize(Policy = AuthorizationPolicies.CustomerOnly)]
    [ProducesResponseType<CheckoutConfirmResponseDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Confirm([FromBody] CheckoutRequestDto request, CancellationToken cancellationToken)
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

        var customerUserId = GetCurrentUserId();
        if (customerUserId is null)
        {
            return Unauthorized();
        }

        var result = await _checkoutService.ConfirmAsync(
            customerUserId.Value,
            request,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        if (result.Confirmation is not null)
        {
            return StatusCode(StatusCodes.Status201Created, result.Confirmation);
        }

        return ToActionResult(result);
    }

    private IActionResult ToActionResult(CheckoutResultDto result)
    {
        if (result.StoreNotFound)
        {
            return NotFound(new { error = "Store not found." });
        }

        if (result.AddressNotFound)
        {
            return NotFound(new { error = "Endereço de entrega não informado." });
        }

        if (result.StoreClosed)
        {
            return Conflict(new { error = "Store is closed." });
        }

        if (result.StoreBlocked)
        {
            return Conflict(new { error = "Store is blocked due to subscription delinquency." });
        }

        if (result.DeliveryAreaNotCovered)
        {
            return BadRequest(new { error = "Ainda nao entregamos no seu bairro." });
        }

        if (result.InvalidItems)
        {
            return BadRequest(new { error = result.ItemError ?? "Itens do pedido inválidos." });
        }

        if (result.BelowMinimum)
        {
            return BadRequest(new
            {
                error = string.Empty,
                summary = result.Summary
            });
        }

        return Ok(result.Summary);
    }

    private Guid? GetCurrentUserId()
    {
        var subject = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(subject, out var userId) ? userId : null;
    }

    private void SetRefreshCookie(string refreshToken, DateTime refreshTokenExpiresAtUtc)
    {
        Response.Cookies.Append("urbeat.refresh_token", refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = new DateTimeOffset(refreshTokenExpiresAtUtc),
            Path = "/"
        });
    }
}
