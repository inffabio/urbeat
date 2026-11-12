using Urbeat.Domain.Enums;

namespace Urbeat.Application.DTOs;

public sealed class BatchUpsertProductsRequestDto
{
    /// <summary>
    /// Itens que substituem todo o catálogo da loja. A coleção é obrigatória: o endpoint
    /// é destrutivo, então <c>null</c> (ou o campo ausente) deve ser rejeitado na validação
    /// antes de qualquer escrita. Uma coleção vazia continua válida e limpa o catálogo.
    /// </summary>
    public IReadOnlyCollection<BatchProductItemDto>? Items { get; init; }
}

public sealed class BatchProductItemDto
{
    public Guid? Id { get; init; }
    public Guid CategoryId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public string? ImageUrl { get; init; }
    public bool IsAvailable { get; init; } = true;
    public int DisplayOrder { get; init; }

    /// <summary>
    /// Classificação especial única e opcional do item. O endpoint <c>/batch</c> substitui todo o
    /// catálogo da loja, portanto o cliente deve enviar a classificação atual para preservá-la e
    /// usar <c>null</c> para removê-la explicitamente.
    /// </summary>
    public ProductSpecialClassification? SpecialClassification { get; init; }
}
