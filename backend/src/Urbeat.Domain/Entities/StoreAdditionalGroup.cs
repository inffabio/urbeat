namespace Urbeat.Domain.Entities;

public sealed class StoreAdditionalGroup : BaseEntity
{
    public Guid StoreId { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public Store Store { get; set; } = null!;

    public ICollection<StoreAdditional> Additionals { get; set; } = new List<StoreAdditional>();
}
