using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Urbeat.WebApi.Controllers;

[ApiController]
[Route("api/public/stores/{storeId}/catalog")]
[AllowAnonymous]
public sealed class PublicCatalogController : ControllerBase
{
    private readonly IProductReadRepository _productReadRepository;

    public PublicCatalogController(IProductReadRepository productReadRepository)
    {
        _productReadRepository = productReadRepository;
    }

    [HttpGet("categories")]
    [ProducesResponseType<IReadOnlyCollection<ProductCategoryResponseDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListCategories([FromRoute] Guid storeId, CancellationToken cancellationToken)
    {
        var categories = await _productReadRepository.ListCategoriesByStoreAsync(storeId, cancellationToken);
        return Ok(categories);
    }

    [HttpGet("products")]
    [ProducesResponseType<IReadOnlyCollection<ProductResponseDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListProducts([FromRoute] Guid storeId, CancellationToken cancellationToken)
    {
        var products = await _productReadRepository.ListAvailableProductsByStoreAsync(storeId, cancellationToken);
        return Ok(products);
    }

    /// <summary>
    /// Endpoint legado de produtos em destaque. A semântica atual é "Ofertas":
    /// retorna apenas produtos com <c>SpecialClassification == Offers</c>.
    /// </summary>
    [HttpGet("products/featured")]
    [ProducesResponseType<IReadOnlyCollection<ProductResponseDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListFeaturedProducts([FromRoute] Guid storeId, CancellationToken cancellationToken)
    {
        var products = await _productReadRepository.ListFeaturedProductsByStoreAsync(storeId, cancellationToken);
        return Ok(products);
    }
}
