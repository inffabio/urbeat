using Urbeat.Application.DTOs;
using MediatR;

namespace Urbeat.Application.Payments;

public sealed record GetOrderPaymentQuery(Guid CustomerUserId, Guid OrderId) : IRequest<OrderPaymentResponseDto?>;
