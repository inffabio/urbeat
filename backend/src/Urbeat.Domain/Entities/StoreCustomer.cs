namespace Urbeat.Domain.Entities;

public sealed class StoreCustomer : BaseEntity
{
    public Guid StoreId { get; set; }
    public Guid CustomerUserId { get; set; }
    public bool IsActive { get; set; } = true;
}
