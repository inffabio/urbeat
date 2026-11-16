using Urbeat.Application.DTOs.Publish;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Interfaces.Publish;
using Urbeat.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Urbeat.WebApi.Controllers;

[ApiController]
[Route("api/stores/{storeId:guid}/publish")]
[Authorize(Policy = AuthorizationPolicies.StoreAccess)]
public sealed class PublishController : ControllerBase
{
    private readonly IStorePublishService _publishService;
    private readonly IStoreRequestContext _storeContext;

    public PublishController(
        IStorePublishService publishService,
        IStoreRequestContext storeContext)
    {
        _publishService = publishService;
        _storeContext = storeContext;
    }

    [HttpGet("summary")]
    public async Task<ActionResult<StorePublishSummaryDto>> GetPublishSummary(
        Guid storeId,
        CancellationToken cancellationToken)
    {
        // Seller: authenticated claim. Admin: owner of the store resolved from the context. The
        // publication rules themselves are untouched.
        var userId = _storeContext.EffectiveSellerUserId;
        if (userId is null)
            return Unauthorized();

        var summary = await _publishService.GetStorePublishSummaryAsync(storeId, userId.Value, cancellationToken);
        return Ok(summary);
    }

    [HttpPost]
    public async Task<ActionResult> PublishStore(
        Guid storeId,
        CancellationToken cancellationToken)
    {
        var userId = _storeContext.EffectiveSellerUserId;
        if (userId is null)
            return Unauthorized();

        var success = await _publishService.PublishStoreAsync(storeId, userId.Value, cancellationToken);

        if (!success)
            return BadRequest(new { message = "Store does not meet requirements to be published yet" });

        return Ok();
    }
}
