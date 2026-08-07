using Urbeat.Domain.Entities;

namespace Urbeat.Application.DTOs;

public sealed class UpdateOrderStatusRequestDto
{
    public OrderStatus NewStatus { get; init; }

    public string? Notes { get; init; }
}
