using System.Net.Http.Json;
using Urbeat.Application.DTOs;

namespace Urbeat.IntegrationTests.Infrastructure;

/// <summary>
/// Cria uma categoria + produto reais via API para os testes de checkout/pedido,
/// já que o backend recomputa o preço a partir do produto persistido (o cliente
/// não envia mais preço).
/// </summary>
public static class ProductTestHelper
{
    public static async Task<Guid> CreateProductAsync(
        HttpClient sellerClient,
        Guid storeId,
        string name,
        decimal price)
    {
        var categoryResponse = await sellerClient.PostAsJsonAsync(
            $"/api/stores/{storeId}/categories",
            new CreateProductCategoryRequestDto
            {
                Name = $"Categoria {Guid.NewGuid():N}",
                DisplayOrder = 0,
                IsFeatured = false,
            });
        categoryResponse.EnsureSuccessStatusCode();
        var category = await categoryResponse.Content.ReadFromJsonAsync<ProductCategoryResponseDto>();

        var productResponse = await sellerClient.PostAsJsonAsync(
            $"/api/stores/{storeId}/products",
            new CreateProductRequestDto
            {
                CategoryId = category!.Id,
                Name = name,
                Description = "Produto de teste",
                Price = price,
                ImageUrl = "https://example.com/product.jpg",
                IsAvailable = true,
                DisplayOrder = 0,
            });
        productResponse.EnsureSuccessStatusCode();
        var product = await productResponse.Content.ReadFromJsonAsync<ProductResponseDto>();
        return product!.Id;
    }
}
