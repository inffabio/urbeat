using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Urbeat.Application.DTOs.Admin;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Security;
using Urbeat.Infrastructure.Security;
using Urbeat.WebApi.Security;

namespace Urbeat.WebApi.Controllers;

/// <summary>
/// Administrative store directory and the delegated dashboard context. Every action is
/// <see cref="AuthorizationPolicies.AdminOnly"/>; the opaque context token is only returned by the
/// creation action, never by the metadata read, and responses are always <c>no-store</c>.
/// </summary>
[ApiController]
[Route("api/admin/stores")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminStoresController : ControllerBase
{
    private readonly IAdminStoreReadRepository _readRepository;
    private readonly IAdminStoreAccessService _accessService;
    private readonly IAdminStoreAuditWriter _auditWriter;
    private readonly AdminStoreAccessOptions _options;

    public AdminStoresController(
        IAdminStoreReadRepository readRepository,
        IAdminStoreAccessService accessService,
        IAdminStoreAuditWriter auditWriter,
        IOptions<AdminStoreAccessOptions> options)
    {
        _readRepository = readRepository;
        _accessService = accessService;
        _auditWriter = auditWriter;
        _options = options.Value;
    }

    [HttpGet]
    [ProducesResponseType<PagedAdminStoresDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        [FromQuery] AdminStoresQueryDto query,
        CancellationToken cancellationToken)
    {
        var stores = await _readRepository.ListAsync(query, _options.MaxPageSize, cancellationToken);
        return Ok(stores);
    }

    [HttpPost("{storeId:guid}/access-context")]
    [EnableRateLimiting("admin-store-context")]
    [ProducesResponseType<AdminStoreAccessContextDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Open(
        [FromRoute] Guid storeId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] AdminStoreAccessOpenRequestDto? request,
        CancellationToken cancellationToken)
    {
        var actorUserId = GetCurrentUserId();
        if (actorUserId is null)
        {
            return Unauthorized();
        }

        var result = await _accessService.CreateAsync(
            actorUserId.Value,
            storeId,
            request?.Reason,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            cancellationToken);

        if (result.Context is null)
        {
            return ToErrorResult(result.Status);
        }

        return StatusCode(StatusCodes.Status201Created, result.Context);
    }

    [HttpGet("{storeId:guid}/access-context")]
    [EnableRateLimiting("admin-store-context")]
    [ProducesResponseType<AdminStoreAccessContextMetadataDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> GetContext(
        [FromRoute] Guid storeId,
        [FromHeader(Name = AdminStoreContextMiddleware.HeaderName)] string? contextToken,
        CancellationToken cancellationToken)
    {
        var actorUserId = GetCurrentUserId();
        if (actorUserId is null)
        {
            return Unauthorized();
        }

        var validation = await _accessService.ValidateAsync(
            contextToken,
            actorUserId.Value,
            storeId,
            touch: true,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        if (!validation.IsValid)
        {
            await AuditContextDenialAsync(storeId, validation.Status);
            return ToErrorResult(validation.Status);
        }

        return Ok(new AdminStoreAccessContextMetadataDto
        {
            ContextId = validation.ContextId!.Value,
            AdminUserId = validation.AdminUserId!.Value,
            StoreId = validation.StoreId!.Value,
            StoreName = validation.StoreName ?? string.Empty,
            StoreSlug = validation.StoreSlug ?? string.Empty,
            ExpiresAtUtc = validation.ExpiresAtUtc!.Value
        });
    }

    [HttpDelete("{storeId:guid}/access-context")]
    [EnableRateLimiting("admin-store-context")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Close(
        [FromRoute] Guid storeId,
        [FromHeader(Name = AdminStoreContextMiddleware.HeaderName)] string? contextToken,
        CancellationToken cancellationToken)
    {
        var actorUserId = GetCurrentUserId();
        if (actorUserId is null)
        {
            return Unauthorized();
        }

        var status = await _accessService.CloseAsync(
            contextToken,
            actorUserId.Value,
            storeId,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        return status switch
        {
            AdminStoreAccessStatus.Valid or AdminStoreAccessStatus.AlreadyClosed => NoContent(),
            _ => await DenyAsync(storeId, status)
        };
    }

    private async Task<IActionResult> DenyAsync(Guid storeId, AdminStoreAccessStatus status)
    {
        await AuditContextDenialAsync(storeId, status);
        return ToErrorResult(status);
    }

    /// <summary>
    /// Context-lifecycle denials that the service does not audit itself (unknown token and store
    /// mismatch). Expired/role-removed/store-missing are already recorded by the service.
    /// </summary>
    private async Task AuditContextDenialAsync(Guid storeId, AdminStoreAccessStatus status)
    {
        if (status is not (AdminStoreAccessStatus.Invalid or AdminStoreAccessStatus.StoreMismatch))
        {
            return;
        }

        var actorUserId = GetCurrentUserId();
        if (actorUserId is null)
        {
            return;
        }

        var reason = status == AdminStoreAccessStatus.StoreMismatch ? "Mismatch" : "Invalid";
        await _auditWriter.TryWriteAsync(
            actorUserId.Value,
            storeId,
            null,
            null,
            "AdminStoreAccessRejected",
            $"Context request denied: {reason}.",
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            CancellationToken.None);
    }

    private Guid? GetCurrentUserId()
    {
        var subject = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(subject, out var userId) ? userId : null;
    }

    private IActionResult ToErrorResult(AdminStoreAccessStatus status)
    {
        var code = status switch
        {
            AdminStoreAccessStatus.StoreNotFound => AdminStoreErrorCodes.StoreNotFound,
            AdminStoreAccessStatus.StoreOwnerNotFound => AdminStoreErrorCodes.StoreOwnerNotFound,
            AdminStoreAccessStatus.Expired => AdminStoreErrorCodes.ContextExpired,
            AdminStoreAccessStatus.AdminNotAllowed => AdminStoreErrorCodes.ContextAdminNotAllowed,
            AdminStoreAccessStatus.StoreMissing => AdminStoreErrorCodes.ContextStoreMissing,
            AdminStoreAccessStatus.StoreMismatch => AdminStoreErrorCodes.ContextStoreMismatch,
            _ => AdminStoreErrorCodes.ContextInvalid
        };

        var payload = new { code, message = AdminStoreErrorCodes.MessageFor(code) };

        return status switch
        {
            AdminStoreAccessStatus.StoreNotFound => NotFound(payload),
            AdminStoreAccessStatus.StoreOwnerNotFound => Conflict(payload),
            _ => StatusCode(StatusCodes.Status403Forbidden, payload)
        };
    }
}
