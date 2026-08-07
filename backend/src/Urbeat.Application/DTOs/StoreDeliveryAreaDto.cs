namespace Urbeat.Application.DTOs;

public sealed class StoreDeliveryAreaDto
{
    public Guid Id { get; init; }
    public string Neighborhood { get; init; } = string.Empty;
    public decimal DeliveryFee { get; init; }
    public decimal MinimumOrderValue { get; init; }
    public decimal? FreeShippingThreshold { get; init; }
    public bool IsActive { get; init; } = true;
    public string Notes { get; init; } = string.Empty;
}
