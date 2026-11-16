using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Security;

namespace Urbeat.WebApi.Controllers;

/// <summary>
/// Administrative management of administrator accounts. Authorization is always
/// <see cref="AuthorizationPolicies.AdminOnly"/>; the actor is read exclusively from the
/// authenticated claim and re-validated against the identity store on every action, so a token
/// issued before the role was removed cannot keep managing administrators. Responses never contain
/// a token, hash, password or security stamp.
/// </summary>
[ApiController]
[Route("api/admin/administrators")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminAccountsController : ControllerBase, IAsyncActionFilter
{
    private const string AdminRole = "Admin";

    private readonly IValidator<InviteAdminAccountRequestDto> _inviteValidator;
    private readonly IValidator<UpdateAdminEmailRequestDto> _updateEmailValidator;
    private readonly IValidator<AdminAccountBlockRequestDto> _blockValidator;
    private readonly IAdminAccountService _adminAccountService;
    private readonly UserManager<IdentityUser<Guid>> _userManager;

    public AdminAccountsController(
        IValidator<InviteAdminAccountRequestDto> inviteValidator,
        IValidator<UpdateAdminEmailRequestDto> updateEmailValidator,
        IValidator<AdminAccountBlockRequestDto> blockValidator,
        IAdminAccountService adminAccountService,
        UserManager<IdentityUser<Guid>> userManager)
    {
        _inviteValidator = inviteValidator;
        _updateEmailValidator = updateEmailValidator;
        _blockValidator = blockValidator;
        _adminAccountService = adminAccountService;
        _userManager = userManager;
    }

    /// <summary>
    /// The JWT role claim alone is not enough for destructive management: the actor identity must
    /// still exist and still own the Admin role. A blocked administrator keeps the role, so support
    /// cases (last-active-admin guard) remain testable; a removed identity is rejected immediately.
    /// </summary>
    async Task IAsyncActionFilter.OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        if (!TryGetActorId(out var actorId))
        {
            context.Result = Forbid();
            return;
        }

        var actor = await _userManager.FindByIdAsync(actorId.ToString());
        if (actor is null
            || !actor.EmailConfirmed
            || !await _userManager.IsInRoleAsync(actor, AdminRole))
        {
            context.Result = Forbid();
            return;
        }

        await next();
    }

    [HttpGet]
    [ProducesResponseType<PagedAdminAdministratorsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorId(out var actorId))
        {
            return Forbid();
        }

        var result = await _adminAccountService.ListAsync(
            new AdminAccountsQueryDto
            {
                Status = status,
                Search = search,
                Page = page ?? 1,
                PageSize = pageSize ?? 20
            },
            actorId,
            cancellationToken);

        return Ok(result);
    }

    [HttpPost]
    [EnableRateLimiting("email-resend")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Invite(
        [FromBody] InviteAdminAccountRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorId(out var actorId))
        {
            return Forbid();
        }

        var validation = await _inviteValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validation.Errors
                .GroupBy(error => error.PropertyName)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(error => error.ErrorMessage).ToArray())));
        }

        var result = await _adminAccountService.InviteAsync(
            request.Email,
            actorId,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        if (!result.Succeeded || result.Administrator is null)
        {
            return ToErrorResult(result.Status, result.Code, result.Error);
        }

        return StatusCode(StatusCodes.Status201Created, new
        {
            administratorId = result.Administrator.Id,
            email = result.Administrator.Email,
            status = result.Administrator.Status,
            invitation = result.Invitation
        });
    }

    [HttpPut("{id:guid}/email")]
    [EnableRateLimiting("email-resend")]
    [ProducesResponseType<AdminAdministratorDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> UpdateEmail(
        Guid id,
        [FromBody] UpdateAdminEmailRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorId(out var actorId))
        {
            return Forbid();
        }

        var validation = await _updateEmailValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validation.Errors
                .GroupBy(error => error.PropertyName)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(error => error.ErrorMessage).ToArray())));
        }

        var result = await _adminAccountService.UpdateEmailAsync(
            id,
            request.Email,
            actorId,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        if (!result.Succeeded || result.Administrator is null)
        {
            return ToErrorResult(result.Status, result.Code, result.Error);
        }

        return Ok(result.Administrator);
    }

    [HttpPost("{id:guid}/invitation/resend")]
    [EnableRateLimiting("email-resend")]
    [ProducesResponseType<AdminAccountResendInvitationResponseDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ResendInvitation(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetActorId(out var actorId))
        {
            return Forbid();
        }

        var result = await _adminAccountService.ResendInvitationAsync(
            id,
            actorId,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        if (!result.Succeeded)
        {
            return ToErrorResult(result.Status, result.Code, result.Error);
        }

        return Accepted(new AdminAccountResendInvitationResponseDto
        {
            InvitationId = result.InvitationId ?? Guid.Empty,
            ExpiresAtUtc = result.ExpiresAtUtc ?? default,
            EmailDeliveryPending = result.EmailDeliveryPending
        });
    }

    [HttpDelete("{id:guid}/invitation")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> CancelInvitation(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetActorId(out var actorId))
        {
            return Forbid();
        }

        var result = await _adminAccountService.CancelInvitationAsync(
            id,
            actorId,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        return result.Succeeded
            ? NoContent()
            : ToErrorResult(result.Status, result.Code, result.Error);
    }

    [HttpPost("{id:guid}/password-reset")]
    [EnableRateLimiting("email-resend")]
    [ProducesResponseType<AdminAccountResendInvitationResponseDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> RequestPasswordReset(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetActorId(out var actorId))
        {
            return Forbid();
        }

        var result = await _adminAccountService.RequestPasswordResetAsync(
            id,
            actorId,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        if (!result.Succeeded)
        {
            return ToErrorResult(result.Status, result.Code, result.Error);
        }

        return Accepted(new AdminAccountResendInvitationResponseDto
        {
            InvitationId = result.InvitationId ?? Guid.Empty,
            ExpiresAtUtc = result.ExpiresAtUtc ?? default,
            EmailDeliveryPending = result.EmailDeliveryPending
        });
    }

    [HttpPost("{id:guid}/block")]
    [ProducesResponseType<AdminAdministratorDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Block(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] AdminAccountBlockRequestDto? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorId(out var actorId))
        {
            return Forbid();
        }

        var payload = request ?? new AdminAccountBlockRequestDto();
        var validation = await _blockValidator.ValidateAsync(payload, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validation.Errors
                .GroupBy(error => error.PropertyName)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(error => error.ErrorMessage).ToArray())));
        }

        var result = await _adminAccountService.BlockAsync(
            id,
            payload.Reason,
            actorId,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        if (!result.Succeeded || result.Administrator is null)
        {
            return ToErrorResult(result.Status, result.Code, result.Error);
        }

        return Ok(result.Administrator);
    }

    [HttpPost("{id:guid}/unblock")]
    [ProducesResponseType<AdminAdministratorDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Unblock(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetActorId(out var actorId))
        {
            return Forbid();
        }

        var result = await _adminAccountService.UnblockAsync(
            id,
            actorId,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        if (!result.Succeeded || result.Administrator is null)
        {
            return ToErrorResult(result.Status, result.Code, result.Error);
        }

        return Ok(result.Administrator);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Remove(
        Guid id,
        [FromQuery] string? reason,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorId(out var actorId))
        {
            return Forbid();
        }

        var result = await _adminAccountService.RemoveAsync(
            id,
            reason,
            actorId,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        return result.Succeeded
            ? NoContent()
            : ToErrorResult(result.Status, result.Code, result.Error);
    }

    private bool TryGetActorId(out Guid actorId)
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claim, out actorId) && actorId != Guid.Empty;
    }

    private IActionResult ToErrorResult(
        AdminAccountOperationStatus status,
        string? code,
        string? message)
    {
        var payload = new { code, message };

        return status switch
        {
            AdminAccountOperationStatus.NotFound => NotFound(payload),
            AdminAccountOperationStatus.SelfActionNotAllowed
                or AdminAccountOperationStatus.InvalidEmail => BadRequest(payload),
            AdminAccountOperationStatus.EmailAlreadyInUse
                or AdminAccountOperationStatus.NotActive
                or AdminAccountOperationStatus.AlreadyBlocked
                or AdminAccountOperationStatus.NotBlocked
                or AdminAccountOperationStatus.LastActiveAdmin
                or AdminAccountOperationStatus.InvitationNotPending => Conflict(payload),
            AdminAccountOperationStatus.Failed => StatusCode(
                StatusCodes.Status500InternalServerError,
                payload),
            _ => BadRequest(payload)
        };
    }
}
