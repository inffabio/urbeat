using Urbeat.Domain.Entities;

namespace Urbeat.Application.DTOs;

public sealed class SellerDeliverySummaryResponseDto
{
    public Guid Id { get; init; }

    public string Code { get; init; } = string.Empty;

    public string? CustomerName { get; init; }

    public string? CustomerPhoneNumber { get; init; }

    public string? AddressSummary { get; init; }

    public OrderStatus Status { get; init; }

    public decimal Total { get; init; }

    public DateTime CreatedAtUtc { get; init; }
}
