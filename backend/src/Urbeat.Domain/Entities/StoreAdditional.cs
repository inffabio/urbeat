namespace Urbeat.Domain.Entities;

public sealed class StoreAdditional : BaseEntity
{
    public Guid StoreId { get; set; }

    public Guid GroupId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public bool IsActive { get; set; } = true;

    public int DisplayOrder { get; set; }

    public Store Store { get; set; } = null!;

    public StoreAdditionalGroup Group { get; set; } = null!;

    public ICollection<ProductAdditionalAssignment> ProductAssignments { get; set; } = new List<ProductAdditionalAssignment>();
}
