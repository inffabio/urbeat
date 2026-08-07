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
[Route("api/stores/{storeId}/categories")]
[Authorize(Policy = AuthorizationPolicies.SellerOnly)]
public sealed class StoreCategoriesController : ControllerBase
{
    private readonly IProductCategoryService _categoryService;
    private readonly IValidator<CreateProductCategoryRequestDto> _createValidator;
    private readonly IValidator<UpdateProductCategoryRequestDto> _updateValidator;

    public StoreCategoriesController(
        IProductCategoryService categoryService,
        IValidator<CreateProductCategoryRequestDto> createValidator,
        IValidator<UpdateProductCategoryRequestDto> updateValidator)
    {
        _categoryService = categoryService;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyCollection<ProductCategoryResponseDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromRoute] Guid storeId, CancellationToken cancellationToken)
    {
        var ownerUserId = GetCurrentUserId();
        if (ownerUserId is null)
            return Unauthorized();

        var categories = await _categoryService.ListByStoreAsync(ownerUserId.Value, storeId, cancellationToken);
        return Ok(categories);
    }

    [HttpPost]
    [ProducesResponseType<ProductCategoryResponseDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create(
        [FromRoute] Guid storeId,
        [FromBody] CreateProductCategoryRequestDto request,
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

        var result = await _categoryService.CreateAsync(ownerUserId.Value, storeId, request,
            HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        if (result.NotFound)
            return NotFound();
        if (result.Forbidden)
            return Forbid();
        if (result.Conflict)
            return Conflict(new { detail = "Já existe uma categoria com este nome para esta loja." });

        return StatusCode(StatusCodes.Status201Created, result.Category);
    }

    [HttpPut("{categoryId}")]
    [ProducesResponseType<ProductCategoryResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        [FromRoute] Guid categoryId,
        [FromBody] UpdateProductCategoryRequestDto request,
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

        var result = await _categoryService.UpdateAsync(ownerUserId.Value, categoryId, request,
            HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        if (result.NotFound)
            return NotFound();
        if (result.Forbidden)
            return Forbid();
        if (result.Conflict)
            return Conflict(new { detail = "Já existe uma categoria com este nome para esta loja." });

        return Ok(result.Category);
    }

    [HttpPut("reorder")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reorder(
        [FromRoute] Guid storeId,
        [FromBody] ReorderStoreCategoriesRequestDto items,
        CancellationToken cancellationToken)
    {
        if (items is null || items.Count == 0)
            return BadRequest(new { detail = "Nenhum item de ordenação informado." });

        var ownerUserId = GetCurrentUserId();
        if (ownerUserId is null)
            return Unauthorized();

        var result = await _categoryService.ReorderAsync(ownerUserId.Value, storeId, items,
            HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        if (result.NotFound) return NotFound();
        if (result.Forbidden) return Forbid();
        if (result.Invalid) return BadRequest(new { detail = "A ordenação deve conter todas as categorias uma única vez, com posições consecutivas." });

        return NoContent();
    }

    [HttpDelete("{categoryId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        [FromRoute] Guid categoryId,
        [FromQuery] Guid? reassignCategoryId = null,
        CancellationToken cancellationToken = default)
    {
        var ownerUserId = GetCurrentUserId();
        if (ownerUserId is null)
            return Unauthorized();

        var deleted = await _categoryService.DeleteAsync(ownerUserId.Value, categoryId,
            reassignCategoryId,
            HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        return deleted switch
        {
            ProductCategoryDeleteResult.Deleted => NoContent(),
            ProductCategoryDeleteResult.Forbidden => Forbid(),
            ProductCategoryDeleteResult.HasProducts => Conflict(new { detail = "A categoria possui produtos associados." }),
            _ => NotFound(),
        };
    }

    private Guid? GetCurrentUserId()
    {
        var subject = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(subject, out var userId) ? userId : null;
    }
}
