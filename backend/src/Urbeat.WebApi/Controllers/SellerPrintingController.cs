using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Urbeat.WebApi.Controllers;

[ApiController]
[Route("api/seller/printing/jobs")]
[Authorize(Policy = AuthorizationPolicies.SellerOnly)]
public sealed class SellerPrintingController : ControllerBase
{
    private readonly IOrderPrintingService _orderPrintingService;

    public SellerPrintingController(IOrderPrintingService orderPrintingService)
    {
        _orderPrintingService = orderPrintingService;
    }

    [HttpGet]
    [ProducesResponseType<PendingPrintJobsResponseDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListPending(
        [FromQuery] long afterCursor = 0,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var sellerUserId = GetCurrentUserId();
        if (sellerUserId is null) return Unauthorized();

        var result = await _orderPrintingService.ListPendingAsync(
            sellerUserId.Value, afterCursor, limit, cancellationToken);
        return Ok(result);
    }

    [HttpPost("{orderId:guid}/result")]
    [ProducesResponseType<RecordPrintJobResultResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RecordResult(
        [FromRoute] Guid orderId,
        [FromBody] RecordPrintJobResultRequestDto request,
        CancellationToken cancellationToken)
    {
        var sellerUserId = GetCurrentUserId();
        if (sellerUserId is null) return Unauthorized();

        var result = await _orderPrintingService.RecordResultAsync(
            sellerUserId.Value, orderId, request, cancellationToken);
        return result.NotFound ? NotFound() : Ok(result);
    }

    private Guid? GetCurrentUserId()
    {
        var subject = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(subject, out var userId) ? userId : null;
    }
}
