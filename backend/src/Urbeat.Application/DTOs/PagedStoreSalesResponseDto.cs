namespace Urbeat.Application.DTOs;

public sealed class PagedStoreSalesResponseDto
{
    public string Period { get; set; } = string.Empty;

    public DateOnly AnchorDate { get; set; }

    public int? WeekBlock { get; set; }

    public DateTime RangeStartUtc { get; set; }

    public DateTime RangeEndUtcExclusive { get; set; }

    public string TimeZone { get; set; } = string.Empty;

    public int Page { get; set; }

    public int PageSize { get; set; }

    public int TotalItems { get; set; }

    public int TotalPages { get; set; }

    public StoreSalesTotalsDto Totals { get; set; } = new();

    public IReadOnlyCollection<StoreSaleRowDto> Sales { get; set; } = [];
}

public sealed class StoreSalesTotalsDto
{
    /// <summary>Sum of Order.Total for every filtered order; already includes delivery fees.</summary>
    public decimal SalesTotal { get; set; }

    /// <summary>Sum of Order.DeliveryFee; a subset of <see cref="SalesTotal"/>, never added to it.</summary>
    public decimal DeliveryFees { get; set; }
}

public sealed class StoreSaleRowDto
{
    public Guid OrderId { get; set; }

    public string Code { get; set; } = string.Empty;

    /// <summary>Only PII exposed by this contract: the customer's FullName claim.</summary>
    public string? CustomerName { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public decimal Subtotal { get; set; }

    public decimal DeliveryFee { get; set; }

    /// <summary>Order total, already including <see cref="DeliveryFee"/>.</summary>
    public decimal Total { get; set; }

    public IReadOnlyCollection<StoreSaleItemDto> Items { get; set; } = [];
}

public sealed class StoreSaleItemDto
{
    public string ProductName { get; set; } = string.Empty;

    public string? VariationName { get; set; }

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal TotalPrice { get; set; }
}
