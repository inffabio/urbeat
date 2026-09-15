namespace Urbeat.Domain.Entities;

/// <summary>Item ordenado de um grupo de opções reutilizável.</summary>
public sealed class ProductOptionItemTemplate : BaseEntity
{
    public Guid GroupId { get; set; }

    public string Name { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public int DisplayOrder { get; set; }

    public ProductOptionGroupTemplate Group { get; set; } = null!;
}
