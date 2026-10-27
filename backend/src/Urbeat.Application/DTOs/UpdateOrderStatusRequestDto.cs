using Urbeat.Domain.Entities;

namespace Urbeat.Application.DTOs;

public sealed class UpdateOrderStatusRequestDto
{
    public OrderStatus NewStatus { get; init; }

    public string? Notes { get; init; }

    /// <summary>
    /// Store-scoped driver link (StoreDeliveryDriver.Id) required when the seller moves a delivery
    /// order to <see cref="OrderStatus.OnDelivery"/>. Ignored for every other transition.
    /// </summary>
    public Guid? DeliveryDriverId { get; init; }
}
