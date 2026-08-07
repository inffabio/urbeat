using Urbeat.Application.DTOs;
using MediatR;

namespace Urbeat.Application.Payments;

public sealed record GetOrderPaymentHistoryQuery(Guid CustomerUserId, Guid OrderId)
    : IRequest<IReadOnlyCollection<PaymentStatusHistoryResponseDto>>;
