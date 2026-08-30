namespace Urbeat.Application.DTOs;

/// <summary>
/// Snapshot de uma opção selecionada de um item de pedido, com nome e preço incremental.
/// </summary>
public sealed class OrderItemOptionPriceDto
{
    public string Name { get; init; } = string.Empty;

    public decimal Price { get; init; }
}
