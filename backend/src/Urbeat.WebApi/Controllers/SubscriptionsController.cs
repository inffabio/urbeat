using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Urbeat.WebApi.Controllers;

[ApiController]
[Route("api/subscriptions")]
public sealed class SubscriptionsController : ControllerBase
{
    private readonly IPlanService _planService;
    private readonly ISellerSubscriptionStatusService _sellerSubscriptionStatusService;
    private readonly IStoreRequestContext _storeContext;

    public SubscriptionsController(
        IPlanService planService,
        ISellerSubscriptionStatusService sellerSubscriptionStatusService,
        IStoreRequestContext storeContext)
    {
        _planService = planService;
        _sellerSubscriptionStatusService = sellerSubscriptionStatusService;
        _storeContext = storeContext;
    }

    // Contracting/cancelling a plan is not delegable: the administrator keeps read-only access.
    [HttpGet("plans")]
    [Authorize(Policy = AuthorizationPolicies.SellerOnly)]
    [ProducesResponseType<IReadOnlyList<PlanResponseDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListActivePlans(CancellationToken cancellationToken)
    {
        var plans = await _planService.ListActiveAsync(cancellationToken);
        return Ok(plans);
    }

    [HttpPost("contract")]
    [Authorize(Policy = AuthorizationPolicies.SellerOnly)]
    [ProducesResponseType<ContractSellerSubscriptionResponseDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Contract(
        [FromBody] ContractSellerSubscriptionRequestDto request,
        CancellationToken cancellationToken)
    {
        var sellerUserId = _storeContext.EffectiveSellerUserId;
        if (sellerUserId is null)
        {
            return Unauthorized();
        }

        var result = await _sellerSubscriptionStatusService.ContractAsync(
            sellerUserId.Value,
            request,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        if (result.NotFound)
        {
            return NotFound();
        }

        if (result.Forbidden)
        {
            return Forbid();
        }

        if (result.InvalidPlan)
        {
            return BadRequest(new { error = "Plan is invalid or inactive." });
        }

        if (result.AlreadyContracted)
        {
            return Conflict(new { error = "Store already has a contracted subscription." });
        }

        return StatusCode(StatusCodes.Status201Created, result.Subscription);
    }

    [HttpGet("my")]
    [Authorize(Policy = AuthorizationPolicies.StoreAccess)]
    [ProducesResponseType<SellerSubscriptionMyResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMySubscription(CancellationToken cancellationToken)
    {
        var sellerUserId = _storeContext.EffectiveSellerUserId;
        if (sellerUserId is null)
        {
            return Unauthorized();
        }

        var response = await _sellerSubscriptionStatusService.GetMySubscriptionAsync(sellerUserId.Value, cancellationToken);
        return Ok(response);
    }

    [HttpGet("my/charges")]
    [Authorize(Policy = AuthorizationPolicies.StoreAccess)]
    [ProducesResponseType<IReadOnlyList<SellerSubscriptionChargeHistoryItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMyChargeHistory(CancellationToken cancellationToken)
    {
        var sellerUserId = _storeContext.EffectiveSellerUserId;
        if (sellerUserId is null)
        {
            return Unauthorized();
        }

        var response = await _sellerSubscriptionStatusService.ListMyChargeHistoryAsync(sellerUserId.Value, cancellationToken);
        return Ok(response);
    }
}
