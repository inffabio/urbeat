using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Services.Payments;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.Infrastructure.Services;

public sealed class PaymentService : IPaymentService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IEfUnitOfWork _efUnitOfWork;
    private readonly IOrderPaymentStrategyFactory _strategyFactory;

    public PaymentService(
        ApplicationDbContext dbContext,
        IEfUnitOfWork efUnitOfWork,
        IOrderPaymentStrategyFactory strategyFactory)
    {
        _dbContext = dbContext;
        _efUnitOfWork = efUnitOfWork;
        _strategyFactory = strategyFactory;
    }

    public async Task<CreateOrderPaymentResultDto> CreateOrderPaymentAsync(
        Guid customerUserId,
        CreateOrderPaymentRequestDto request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.Orders
            .SingleOrDefaultAsync(x => x.Id == request.OrderId && x.CustomerUserId == customerUserId, cancellationToken);

        if (order is null)
        {
            return new CreateOrderPaymentResultDto { NotFound = true };
        }

        var strategy = _strategyFactory.Resolve(order.PaymentMethod);
        if (strategy is null)
        {
            return new CreateOrderPaymentResultDto { UnsupportedMethod = true };
        }

        if (order.Status != OrderStatus.PendingPayment)
        {
            return new CreateOrderPaymentResultDto { InvalidOrderState = true };
        }

        var existingPayment = await _dbContext.Payments
            .SingleOrDefaultAsync(x => x.OrderId == order.Id, cancellationToken);

        var payment = await strategy.StartAsync(order, existingPayment, cancellationToken);

        var paymentEntity = await _dbContext.Payments
            .SingleOrDefaultAsync(x => x.Id == payment.PaymentId, cancellationToken)
            ?? _dbContext.Payments.Local.SingleOrDefault(x => x.Id == payment.PaymentId);

        if (paymentEntity is null)
        {
            throw new InvalidOperationException("Payment entity could not be resolved for status history registration.");
        }

        var hasHistory = await _dbContext.PaymentStatusHistories
            .AsNoTracking()
            .AnyAsync(x => x.PaymentId == paymentEntity.Id, cancellationToken);

        if (!hasHistory)
        {
            await _dbContext.PaymentStatusHistories.AddAsync(new PaymentStatusHistory
            {
                PaymentId = paymentEntity.Id,
                PreviousStatus = null,
                NewStatus = paymentEntity.Status,
                Source = "Checkout",
                Notes = "Initial payment status created when checkout was started.",
                RawPayload = paymentEntity.RawPayload
            }, cancellationToken);
        }

        await _dbContext.AuditLogs.AddAsync(new AuditLog
        {
            UserId = customerUserId,
            Event = "OrderPaymentRequested",
            Entity = nameof(Order),
            EntityId = order.Id,
            Description = "Customer started online payment.",
            IpAddress = ipAddress
        }, cancellationToken);

        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        return new CreateOrderPaymentResultDto
        {
            Payment = payment
        };
    }

    public async Task<OrderPaymentResponseDto?> GetOrderPaymentAsync(
        Guid customerUserId,
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var payment = await _dbContext.Payments
            .AsNoTracking()
            .Join(
                _dbContext.Orders.AsNoTracking().Where(x => x.CustomerUserId == customerUserId),
                payment => payment.OrderId,
                order => order.Id,
                (payment, _) => payment)
            .SingleOrDefaultAsync(x => x.OrderId == orderId, cancellationToken);

        if (payment is null)
        {
            return null;
        }

        var history = await _dbContext.PaymentStatusHistories
            .AsNoTracking()
            .Where(x => x.PaymentId == payment.Id)
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => new PaymentStatusHistoryResponseDto
            {
                CreatedAtUtc = x.CreatedAtUtc,
                PreviousStatus = x.PreviousStatus,
                NewStatus = x.NewStatus,
                Source = x.Source,
                Notes = x.Notes
            })
            .ToListAsync(cancellationToken);

        return new OrderPaymentResponseDto
        {
            PaymentId = payment.Id,
            OrderId = payment.OrderId,
            Gateway = payment.Gateway,
            GatewayTransactionId = payment.GatewayTransactionId,
            GatewayCheckoutUrl = payment.GatewayCheckoutUrl,
            Method = payment.Method,
            Status = payment.Status,
            Amount = payment.Amount,
            CreatedAtUtc = payment.CreatedAtUtc,
            UpdatedAtUtc = payment.UpdatedAtUtc,
            History = history
        };
    }

    public async Task<IReadOnlyCollection<PaymentStatusHistoryResponseDto>> ListOrderPaymentHistoryAsync(
        Guid customerUserId,
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.PaymentStatusHistories
            .AsNoTracking()
            .Join(
                _dbContext.Payments.AsNoTracking(),
                history => history.PaymentId,
                payment => payment.Id,
                (history, payment) => new { history, payment })
            .Join(
                _dbContext.Orders.AsNoTracking().Where(x => x.CustomerUserId == customerUserId && x.Id == orderId),
                hp => hp.payment.OrderId,
                order => order.Id,
                (hp, _) => hp.history)
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => new PaymentStatusHistoryResponseDto
            {
                CreatedAtUtc = x.CreatedAtUtc,
                PreviousStatus = x.PreviousStatus,
                NewStatus = x.NewStatus,
                Source = x.Source,
                Notes = x.Notes
            })
            .ToListAsync(cancellationToken);
    }
}
