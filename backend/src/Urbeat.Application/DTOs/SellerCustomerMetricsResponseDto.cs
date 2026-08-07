namespace Urbeat.Application.DTOs;

public sealed class SellerCustomerMetricsResponseDto
{
    public int TotalCustomers { get; init; }

    public int ActiveCustomers { get; init; }

    public int RecurringCustomers { get; init; }

    public int NewCustomersThisMonth { get; init; }

    public decimal AverageTicket { get; init; }
}
