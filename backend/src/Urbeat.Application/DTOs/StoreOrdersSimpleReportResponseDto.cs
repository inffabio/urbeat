namespace Urbeat.Application.DTOs;

public sealed class StoreOrdersSimpleReportResponseDto
{
    public int TotalOrders { get; set; }
    public decimal TotalRevenue { get; set; }
    public int InProgressOrders { get; set; }
    public DateTime? StartDateUtc { get; set; }
    public DateTime? EndDateUtc { get; set; }
}
