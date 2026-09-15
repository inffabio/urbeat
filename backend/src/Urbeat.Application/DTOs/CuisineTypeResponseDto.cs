namespace Urbeat.Application.DTOs;

public sealed class CuisineTypeResponseDto
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    // Categorias padrão protegidas não podem ser alteradas/excluídas por fluxos de loja.
    public bool IsDefault { get; init; }

    // Nulo para categorias padrão globais; preenchido para categorias privadas da loja.
    public Guid? StoreId { get; init; }
}