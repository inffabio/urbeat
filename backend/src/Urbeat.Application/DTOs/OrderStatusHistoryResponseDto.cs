using Urbeat.Domain.Entities;

namespace Urbeat.Application.DTOs;

public sealed class OrderStatusHistoryResponseDto
{
    public DateTime CreatedAtUtc { get; init; }

    public OrderStatus PreviousStatus { get; init; }

    public OrderStatus NewStatus { get; init; }

    public Guid ChangedByUserId { get; init; }

    public string? Notes { get; init; }
}
