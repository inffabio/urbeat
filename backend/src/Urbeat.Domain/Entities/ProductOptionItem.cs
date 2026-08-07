namespace Urbeat.Domain.Entities;

public sealed class ProductOptionItem : BaseEntity
{
    public Guid GroupId { get; set; }

    public string Name { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public int DisplayOrder { get; set; }

    public ProductOptionGroup Group { get; set; } = null!;
}
