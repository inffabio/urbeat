namespace Urbeat.Domain.Entities;

public sealed class ProductCategory : BaseEntity
{
    public Guid StoreId { get; set; }

    public string Name { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsFeatured { get; set; }
}
