namespace Urbeat.Domain.Entities;

public sealed class ProductAdditionalAssignment : BaseEntity
{
    public Guid ProductId { get; set; }

    public Guid AdditionalId { get; set; }

    public Product Product { get; set; } = null!;

    public StoreAdditional Additional { get; set; } = null!;
}
