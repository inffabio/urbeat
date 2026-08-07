namespace Urbeat.Application.DTOs;

public sealed class UpdateStoreDeliveryConfigRequestDto
{
    public decimal DeliveryFee { get; init; }

    public decimal MinimumOrderValue { get; init; }

    public decimal? FreeShippingThreshold { get; init; }

    public bool FreeShippingToday { get; init; }

    public IEnumerable<StoreDeliveryAreaDto>? DeliveryAreas { get; init; }
}
