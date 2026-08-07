namespace Urbeat.Domain.Entities;

public sealed class StoreDeliveryArea : BaseEntity
{
    public Guid StoreId { get; set; }
    public string Neighborhood { get; set; } = string.Empty;
    public decimal DeliveryFee { get; set; }
    public decimal MinimumOrderValue { get; set; }
    public decimal? FreeShippingThreshold { get; set; }
    public bool IsActive { get; set; } = true;
    public string Notes { get; set; } = string.Empty;
}
