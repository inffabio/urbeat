using Urbeat.Application.DTOs;
using Urbeat.Domain.Entities;

namespace Urbeat.Application.Interfaces;

/// <summary>
/// Serviço autoritativo de preços. Calcula o preço unitário de um item de pedido
/// a partir do produto persistido e das seleções (ids) enviadas pelo cliente,
/// aplicando o PriceMode de cada grupo de opções. Nunca confia em preço do cliente.
/// </summary>
public interface IPricingService
{
    ItemPricingResultDto PriceItem(Product product, CheckoutItemRequestDto item);
}
