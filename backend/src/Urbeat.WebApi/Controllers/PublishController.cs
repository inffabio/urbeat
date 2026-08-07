using System.Security.Claims;
using Urbeat.Application.DTOs.Publish;
using Urbeat.Application.Interfaces.Publish;
using Urbeat.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Urbeat.WebApi.Controllers;

[ApiController]
[Route("api/stores/{storeId:guid}/publish")]
[Authorize(Policy = AuthorizationPolicies.SellerOnly)]
public sealed class PublishController : ControllerBase
{
    private readonly IStorePublishService _publishService;

    public PublishController(IStorePublishService publishService)
    {
        _publishService = publishService;
    }

    [HttpGet("summary")]
    public async Task<ActionResult<StorePublishSummaryDto>> GetPublishSummary(
        Guid storeId,
        CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        var summary = await _publishService.GetStorePublishSummaryAsync(storeId, userId, cancellationToken);
        return Ok(summary);
    }

    [HttpPost]
    public async Task<ActionResult> PublishStore(
        Guid storeId,
        CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        var success = await _publishService.PublishStoreAsync(storeId, userId, cancellationToken);
        
        if (!success)
            return BadRequest(new { message = "Store does not meet requirements to be published yet" });
            
        return Ok();
    }
}