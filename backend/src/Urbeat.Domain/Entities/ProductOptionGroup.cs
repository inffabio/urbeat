namespace Urbeat.Domain.Entities;

public sealed class ProductOptionGroup : BaseEntity
{
    public Guid ProductId { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsRequired { get; set; }

    /// <summary>Tipo de seleção: "single" (escolha única) | "multiple" (múltipla escolha).</summary>
    public string ChoiceType { get; set; } = "single";

    public int MinChoices { get; set; }

    public int MaxChoices { get; set; } = 1;

    public int DisplayOrder { get; set; }

    public ICollection<ProductOptionItem> Items { get; set; } = new List<ProductOptionItem>();
}
