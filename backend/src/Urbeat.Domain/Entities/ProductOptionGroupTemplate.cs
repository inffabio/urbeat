namespace Urbeat.Domain.Entities;

/// <summary>
/// Definição reutilizável de um grupo de opções pertencente a uma loja.
/// Cada produto mantém seu próprio snapshot independente; alterar um produto
/// não modifica o template nem os demais produtos.
/// </summary>
public sealed class ProductOptionGroupTemplate : BaseEntity
{
    public Guid StoreId { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsRequired { get; set; }

    /// <summary>Tipo de seleção: "single" (escolha única) | "multiple" (múltipla escolha).</summary>
    public string ChoiceType { get; set; } = "single";

    public int MinChoices { get; set; }

    public int MaxChoices { get; set; } = 1;

    public int DisplayOrder { get; set; }

    public ICollection<ProductOptionItemTemplate> Items { get; set; } = new List<ProductOptionItemTemplate>();
}
