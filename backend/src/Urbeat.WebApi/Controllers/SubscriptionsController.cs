using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Urbeat.WebApi.Controllers;

[ApiController]
[Route("api/subscriptions")]
[Authorize(Policy = AuthorizationPolicies.SellerOnly)]
public sealed class SubscriptionsController : ControllerBase
{
    private readonly IPlanService _planService;
    private readonly ISellerSubscriptionStatusService _sellerSubscriptionStatusService;

    public SubscriptionsController(
        IPlanService planService,
        ISellerSubscriptionStatusService sellerSubscriptionStatusService)
    {
        _planService = planService;
        _sellerSubscriptionStatusService = sellerSubscriptionStatusService;
    }

    [HttpGet("plans")]
    [ProducesResponseType<IReadOnlyList<PlanResponseDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListActivePlans(CancellationToken cancellationToken)
    {
        var plans = await _planService.ListActiveAsync(cancellationToken);
        return Ok(plans);
    }

    [HttpPost("contract")]
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
        var sellerUserId = GetCurrentUserId();
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
    [ProducesResponseType<SellerSubscriptionMyResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMySubscription(CancellationToken cancellationToken)
    {
        var sellerUserId = GetCurrentUserId();
        if (sellerUserId is null)
        {
            return Unauthorized();
        }

        var response = await _sellerSubscriptionStatusService.GetMySubscriptionAsync(sellerUserId.Value, cancellationToken);
        return Ok(response);
    }

    [HttpGet("my/charges")]
    [ProducesResponseType<IReadOnlyList<SellerSubscriptionChargeHistoryItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMyChargeHistory(CancellationToken cancellationToken)
    {
        var sellerUserId = GetCurrentUserId();
        if (sellerUserId is null)
        {
            return Unauthorized();
        }

        var response = await _sellerSubscriptionStatusService.ListMyChargeHistoryAsync(sellerUserId.Value, cancellationToken);
        return Ok(response);
    }

    private Guid? GetCurrentUserId()
    {
        var subject = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(subject, out var userId) ? userId : null;
    }
}
