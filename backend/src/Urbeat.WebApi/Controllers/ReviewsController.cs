using System.Security.Claims;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Urbeat.WebApi.Controllers;

[ApiController]
public sealed class ReviewsController : ControllerBase
{
    private readonly IReviewService _reviewService;

    public ReviewsController(IReviewService reviewService)
    {
        _reviewService = reviewService;
    }

    [HttpPost("api/orders/{orderId}/review")]
    [Authorize(Policy = "CustomerOnly")]
    [ProducesResponseType<ReviewResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateOrUpdate(
        [FromRoute] Guid orderId,
        [FromBody] CreateReviewRequestDto request,
        CancellationToken cancellationToken)
    {
        var customerUserId = GetCurrentUserId();
        if (customerUserId is null)
            return Unauthorized();

        try
        {
            var review = await _reviewService.CreateOrUpdateAsync(
                customerUserId.Value, orderId, request, cancellationToken);
            return Ok(review);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("api/orders/{orderId}/review")]
    [Authorize]
    [ProducesResponseType<ReviewResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByOrder(
        [FromRoute] Guid orderId,
        CancellationToken cancellationToken)
    {
        var customerUserId = GetCurrentUserId();
        if (customerUserId is null)
            return Unauthorized();

        var review = await _reviewService.GetByOrderAsync(
            customerUserId.Value, orderId, cancellationToken);

        return review is not null ? Ok(review) : NotFound();
    }

    [HttpGet("api/public/stores/{storeId}/reviews")]
    [AllowAnonymous]
    [ProducesResponseType<IReadOnlyCollection<StoreReviewResponseDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListByStore(
        [FromRoute] Guid storeId,
        CancellationToken cancellationToken)
    {
        var reviews = await _reviewService.ListByStoreAsync(storeId, cancellationToken);
        return Ok(reviews);
    }

    [HttpGet("api/reviews/store")]
    [Authorize(Policy = AuthorizationPolicies.SellerOnly)]
    [ProducesResponseType<IReadOnlyCollection<StoreReviewResponseDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListBySeller(CancellationToken cancellationToken)
    {
        var sellerUserId = GetCurrentUserId();
        if (sellerUserId is null)
            return Unauthorized();

        var reviews = await _reviewService.ListBySellerAsync(sellerUserId.Value, cancellationToken);
        return Ok(reviews);
    }

    private Guid? GetCurrentUserId()
    {
        var subject = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(subject, out var userId) ? userId : null;
    }
}
