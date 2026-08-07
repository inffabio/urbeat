using Urbeat.Domain.Entities;

namespace Urbeat.Application.DTOs;

public sealed class StoreOrdersHistoryQueryDto
{
    public OrderStatus? Status { get; set; }

    public DateTime? StartDateUtc { get; set; }

    public DateTime? EndDateUtc { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;
}
