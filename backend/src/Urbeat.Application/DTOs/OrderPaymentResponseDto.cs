using Urbeat.Domain.Entities;

namespace Urbeat.Application.DTOs;

public sealed class OrderPaymentResponseDto
{
    public Guid PaymentId { get; init; }

    public Guid OrderId { get; init; }

    public PaymentGateway Gateway { get; init; }

    public string GatewayTransactionId { get; init; } = string.Empty;

    public string? GatewayCheckoutUrl { get; init; }

    public PaymentMethod Method { get; init; }

    public PaymentStatus Status { get; init; }

    public decimal Amount { get; init; }

    public DateTime CreatedAtUtc { get; init; }

    public DateTime? UpdatedAtUtc { get; init; }

    public IReadOnlyCollection<PaymentStatusHistoryResponseDto> History { get; init; } = [];
}
