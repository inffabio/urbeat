using Urbeat.Domain.Entities;

namespace Urbeat.Application.DTOs;

public sealed class StoreAnalyticsResponseDto
{
    public string Period { get; set; } = string.Empty;

    public DateOnly AnchorDate { get; set; }

    public int? WeekBlock { get; set; }

    public DateTime RangeStartUtc { get; set; }

    public DateTime RangeEndUtcExclusive { get; set; }

    public string TimeZone { get; set; } = string.Empty;

    public int ChartYear { get; set; }

    public StoreAnalyticsKpisDto Kpis { get; set; } = new();

    public IReadOnlyCollection<StoreAnalyticsProductDto> Products { get; set; } = [];

    public IReadOnlyCollection<StoreAnalyticsPaymentDto> Payments { get; set; } = [];

    public IReadOnlyCollection<StoreAnalyticsMonthDto> Months { get; set; } = [];

    public IReadOnlyCollection<int> AvailableYears { get; set; } = [];
}

public sealed class StoreAnalyticsKpisDto
{
    public int DeliveredOrders { get; set; }

    public decimal Revenue { get; set; }

    public decimal DeliveryFees { get; set; }

    public decimal AverageTicket { get; set; }
}

public sealed class StoreAnalyticsProductDto
{
    public string ProductName { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public decimal Revenue { get; set; }
}

public sealed class StoreAnalyticsPaymentDto
{
    public PaymentMethod PaymentMethod { get; set; }

    public int DeliveredOrders { get; set; }

    public decimal Revenue { get; set; }
}

public sealed class StoreAnalyticsMonthDto
{
    public int Year { get; set; }

    public int Month { get; set; }

    public int DeliveredOrders { get; set; }

    public decimal Revenue { get; set; }
}
