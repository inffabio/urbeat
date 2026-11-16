using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;

namespace Urbeat.WebApi.Controllers;

/// <summary>
/// Public endpoints reached from the administrative invitation/reset e-mail links. The token
/// travels only in the request body (never in path/query), so it cannot leak into access logs.
/// </summary>
[ApiController]
[Route("api/auth/admin-account")]
[AllowAnonymous]
[EnableRateLimiting("password-recovery")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminAccountSetupController : ControllerBase
{
    private const string GenericInvalidMessage =
        "Este link é inválido ou expirou. Peça um novo convite a um administrador.";

    private readonly IValidator<AdminAccountTokenRequestDto> _tokenValidator;
    private readonly IValidator<AdminAccountAcceptRequestDto> _acceptValidator;
    private readonly IAdminAccountService _adminAccountService;

    public AdminAccountSetupController(
        IValidator<AdminAccountTokenRequestDto> tokenValidator,
        IValidator<AdminAccountAcceptRequestDto> acceptValidator,
        IAdminAccountService adminAccountService)
    {
        _tokenValidator = tokenValidator;
        _acceptValidator = acceptValidator;
        _adminAccountService = adminAccountService;
    }

    [HttpPost("invitation/validate")]
    [ProducesResponseType<AdminInvitationValidationDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> ValidateInvitation(
        [FromBody] AdminAccountTokenRequestDto? request,
        CancellationToken cancellationToken)
        => ValidateAsync(request, AdminInvitationPurpose.AccountInvitation, cancellationToken);

    [HttpPost("invitation/accept")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public Task<IActionResult> AcceptInvitation(
        [FromBody] AdminAccountAcceptRequestDto request,
        CancellationToken cancellationToken)
        => ConsumeAsync(request, AdminInvitationPurpose.AccountInvitation, cancellationToken);

    [HttpPost("password-reset/validate")]
    [ProducesResponseType<AdminInvitationValidationDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> ValidatePasswordReset(
        [FromBody] AdminAccountTokenRequestDto? request,
        CancellationToken cancellationToken)
        => ValidateAsync(request, AdminInvitationPurpose.PasswordReset, cancellationToken);

    [HttpPost("password-reset/confirm")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public Task<IActionResult> ConfirmPasswordReset(
        [FromBody] AdminAccountAcceptRequestDto request,
        CancellationToken cancellationToken)
        => ConsumeAsync(request, AdminInvitationPurpose.PasswordReset, cancellationToken);

    private async Task<IActionResult> ValidateAsync(
        AdminAccountTokenRequestDto? request,
        AdminInvitationPurpose purpose,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var token = request?.Token ?? string.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            return Ok(new AdminInvitationValidationDto
            {
                Valid = false,
                Message = GenericInvalidMessage
            });
        }

        var result = await _adminAccountService.ValidateInvitationTokenAsync(
            token,
            purpose,
            cancellationToken);

        // Always 200: a validation call must not become an existence oracle.
        return Ok(result);
    }

    private async Task<IActionResult> ConsumeAsync(
        AdminAccountAcceptRequestDto request,
        AdminInvitationPurpose purpose,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var validation = await _acceptValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            var firstError = validation.Errors.FirstOrDefault()?.ErrorMessage;
            var isTokenProblem = validation.Errors.Any(error =>
                error.PropertyName.Equals(nameof(request.Token), StringComparison.OrdinalIgnoreCase));

            return BadRequest(new
            {
                code = isTokenProblem
                    ? AdminAccountErrorCodes.InvalidOrExpiredToken
                    : AdminAccountErrorCodes.WeakPassword,
                message = firstError ?? GenericInvalidMessage
            });
        }

        var result = purpose == AdminInvitationPurpose.PasswordReset
            ? await _adminAccountService.ConfirmPasswordResetAsync(
                request.Token,
                request.NewPassword,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                cancellationToken)
            : await _adminAccountService.AcceptInvitationAsync(
                request.Token,
                request.NewPassword,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                cancellationToken);

        if (result.Succeeded)
        {
            return Ok(new
            {
                succeeded = true,
                purpose = result.Purpose,
                message = result.Message
            });
        }

        return result.Code == AdminAccountErrorCodes.TokenAlreadyUsed
            ? Conflict(new { code = result.Code, message = result.Error })
            : BadRequest(new
            {
                code = result.Code ?? AdminAccountErrorCodes.InvalidOrExpiredToken,
                message = result.Error ?? GenericInvalidMessage
            });
    }
}
