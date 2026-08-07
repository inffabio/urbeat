using Urbeat.Application.DTOs;
using MediatR;

namespace Urbeat.Application.Payments;

public sealed record CreateOrderPaymentCommand(
    Guid CustomerUserId,
    CreateOrderPaymentRequestDto Request,
    string? IpAddress) : IRequest<CreateOrderPaymentResultDto>;
