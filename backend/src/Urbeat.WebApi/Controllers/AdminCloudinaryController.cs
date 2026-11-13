using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Security;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;

namespace Urbeat.WebApi.Controllers;

/// <summary>
/// Administrative maintenance of Cloudinary assets. Scan is read-only; cleanup requires
/// <c>confirm=true</c> and is permanently bounded to assets under <c>stores/</c> that are not
/// referenced by any persisted URL.
/// </summary>
[ApiController]
[Route("api/admin/cloudinary")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public sealed class AdminCloudinaryController : ControllerBase
{
    private readonly IStoreImageMaintenanceService _maintenanceService;
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<AdminCloudinaryController> _logger;

    public AdminCloudinaryController(
        IStoreImageMaintenanceService maintenanceService,
        ApplicationDbContext dbContext,
        ILogger<AdminCloudinaryController> logger)
    {
        _maintenanceService = maintenanceService;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Dry run: lists orphan candidates under <c>stores/</c> and never deletes anything.
    /// </summary>
    [HttpGet("orphan-images")]
    [ProducesResponseType<StoreImageOrphanScanResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ScanOrphanImages(
        [FromQuery] int? maximumAssets,
        [FromQuery] int? minimumAgeMinutes,
        CancellationToken cancellationToken)
    {
        var result = await _maintenanceService.ScanAsync(
            new StoreImageOrphanScanRequestDto
            {
                MaximumAssets = maximumAssets,
                MinimumAgeMinutes = minimumAgeMinutes
            },
            cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Destroys orphan candidates. Requires an explicit confirmation flag in the body; the
    /// service also enforces the flag as defense in depth.
    /// </summary>
    [HttpPost("orphan-images/cleanup")]
    [ProducesResponseType<StoreImageOrphanCleanupResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CleanupOrphanImages(
        [FromBody] StoreImageOrphanCleanupRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!request.Confirm)
        {
            return BadRequest(new
            {
                error = "Confirmation required. Resend the request with confirm=true to delete orphan images.",
                requiresConfirmation = true,
                prefix = "stores/"
            });
        }

        var result = await _maintenanceService.CleanupAsync(request, cancellationToken);
        if (result.ConfirmationRequired)
        {
            return BadRequest(new
            {
                error = "Confirmation required. Resend the request with confirm=true to delete orphan images.",
                requiresConfirmation = true,
                prefix = "stores/"
            });
        }

        await WriteAuditLogAsync(result, cancellationToken);

        return Ok(result);
    }

    private async Task WriteAuditLogAsync(StoreImageOrphanCleanupResultDto result, CancellationToken cancellationToken)
    {
        var subject = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        _dbContext.AuditLogs.Add(new AuditLog
        {
            UserId = Guid.TryParse(subject, out var userId) ? userId : null,
            Event = "CloudinaryOrphanCleanup",
            Entity = "CloudinaryImage",
            EntityId = null,
            Description = $"Cloudinary orphan cleanup under 'stores/' executed. scanned={result.ScannedCount}, orphans={result.OrphanCount}, deleted={result.DeletedCount}, failed={result.FailedCount}, truncated={result.Truncated}.",
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
        });

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            // Deletion already happened and is reported to the caller; an audit write failure must
            // not hide the result.
            _logger.LogError(exception, "Failed to write audit log for Cloudinary orphan cleanup.");
        }
    }
}
