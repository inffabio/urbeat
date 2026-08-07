namespace Urbeat.Domain.Entities;

public sealed class ProductChoiceOption : BaseEntity
{
    public Guid ProductId { get; set; }

    public string Name { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsRequired { get; set; }

    public int DisplayOrder { get; set; }
}
