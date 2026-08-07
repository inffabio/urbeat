using Urbeat.Domain.Entities;

namespace Urbeat.Application.DTOs;

public sealed class PaymentStatusHistoryResponseDto
{
    public DateTime CreatedAtUtc { get; init; }

    public PaymentStatus? PreviousStatus { get; init; }

    public PaymentStatus NewStatus { get; init; }

    public string Source { get; init; } = string.Empty;

    public string? Notes { get; init; }
}
