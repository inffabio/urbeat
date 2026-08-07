using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentValidation;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Urbeat.WebApi.Controllers;

[ApiController]
[Route("api/stores/{storeId}/products")]
[Authorize(Policy = AuthorizationPolicies.SellerOnly)]
public sealed class StoreProductsController : ControllerBase
{
    private readonly IProductService _productService;
    private readonly IValidator<CreateProductRequestDto> _createValidator;
    private readonly IValidator<UpdateProductRequestDto> _updateValidator;
    private readonly IStoreService _storeService;

    private readonly IImageUploadService _imageUploadService;

    public StoreProductsController(
        IProductService productService,
        IValidator<CreateProductRequestDto> createValidator,
        IValidator<UpdateProductRequestDto> updateValidator,
        IStoreService storeService,
        IImageUploadService imageUploadService)
    {
        _productService = productService;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _storeService = storeService;
        _imageUploadService = imageUploadService;
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyCollection<ProductResponseDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromRoute] Guid storeId, CancellationToken cancellationToken)
    {
        var ownerUserId = GetCurrentUserId();
        if (ownerUserId is null)
            return Unauthorized();

        var products = await _productService.ListByStoreAsync(ownerUserId.Value, storeId, cancellationToken);
        return Ok(products);
    }

    [HttpPost]
    [ProducesResponseType<ProductResponseDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create(
        [FromRoute] Guid storeId,
        [FromBody] CreateProductRequestDto request,
        CancellationToken cancellationToken)
    {
        var validation = await _createValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
            return ValidationProblem(new ValidationProblemDetails(validation.Errors
                .GroupBy(x => x.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(x => x.ErrorMessage).ToArray())));

        var ownerUserId = GetCurrentUserId();
        if (ownerUserId is null)
            return Unauthorized();

        var result = await _productService.CreateAsync(ownerUserId.Value, storeId, request,
            HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        if (result.NotFound)
            return NotFound();
        if (result.Forbidden)
            return Forbid();

        BusinessMetrics.ProductsUpdated.Inc();
        return StatusCode(StatusCodes.Status201Created, result.Product);
    }

    [HttpPut("{productId}")]
    [ProducesResponseType<ProductResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        [FromRoute] Guid productId,
        [FromBody] UpdateProductRequestDto request,
        CancellationToken cancellationToken)
    {
        var validation = await _updateValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
            return ValidationProblem(new ValidationProblemDetails(validation.Errors
                .GroupBy(x => x.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(x => x.ErrorMessage).ToArray())));

        var ownerUserId = GetCurrentUserId();
        if (ownerUserId is null)
            return Unauthorized();

        var result = await _productService.UpdateAsync(ownerUserId.Value, productId, request,
            HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        if (result.NotFound)
            return NotFound();
        if (result.Forbidden)
            return Forbid();

        BusinessMetrics.ProductsUpdated.Inc();
        return Ok(result.Product);
    }

    [HttpPatch("{productId}/availability")]
    [ProducesResponseType<ProductResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAvailability(
        [FromRoute] Guid productId,
        [FromBody] UpdateProductAvailabilityRequestDto request,
        CancellationToken cancellationToken)
    {
        var ownerUserId = GetCurrentUserId();
        if (ownerUserId is null)
            return Unauthorized();

        var result = await _productService.UpdateAvailabilityAsync(ownerUserId.Value, productId,
            request.IsAvailable,
            HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        if (result.NotFound)
            return NotFound();
        if (result.Forbidden)
            return Forbid();

        return Ok(result.Product);
    }

    [HttpDelete("{productId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        [FromRoute] Guid productId,
        CancellationToken cancellationToken)
    {
        var ownerUserId = GetCurrentUserId();
        if (ownerUserId is null)
            return Unauthorized();

        var deleted = await _productService.DeleteAsync(ownerUserId.Value, productId,
            HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        if (!deleted)
            return NotFound();

        return NoContent();
    }

    [HttpPost("{productId}/images")]
    [ProducesResponseType<ProductResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [RequestSizeLimit(50 * 1024 * 1024)] // 50MB limit to match Nginx client_max_body_size
    public async Task<IActionResult> UploadImage(
        [FromRoute] Guid productId,
        [FromRoute] Guid storeId,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Nenhum arquivo enviado." });

        var ownerUserId = GetCurrentUserId();
        if (ownerUserId is null)
            return Unauthorized();

        var store = await _storeService.GetByOwnerAsync(ownerUserId.Value, cancellationToken);
        var folder = store is not null ? $"stores/{store.Slug}/products" : $"stores/{storeId}/products";

        string imageUrl;
        try
        {
            await using var stream = file.OpenReadStream();
            imageUrl = await _imageUploadService.UploadAsync(stream, file.FileName, folder, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        var result = await _productService.UpdateImageAsync(ownerUserId.Value, productId, imageUrl,
            HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        if (result.NotFound)
            return NotFound();
        if (result.Forbidden)
            return Forbid();

        return Ok(result.Product);
    }

    [HttpPost("batch")]
    [ProducesResponseType<IReadOnlyCollection<ProductResponseDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> BatchUpsert(
        [FromRoute] Guid storeId,
        [FromBody] BatchUpsertProductsRequestDto request,
        CancellationToken cancellationToken)
    {
        var ownerUserId = GetCurrentUserId();
        if (ownerUserId is null)
            return Unauthorized();

        var products = await _productService.BatchUpsertAsync(ownerUserId.Value, storeId, request,
            HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        BusinessMetrics.ProductsUpdated.Inc();
        return StatusCode(StatusCodes.Status201Created, products);
    }

    private Guid? GetCurrentUserId()
    {
        var subject = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(subject, out var userId) ? userId : null;
    }
}
