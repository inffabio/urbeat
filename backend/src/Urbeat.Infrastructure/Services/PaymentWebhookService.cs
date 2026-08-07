using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Services.Payments;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.Infrastructure.Services;

public sealed class PaymentWebhookService : IPaymentWebhookService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IEfUnitOfWork _efUnitOfWork;
    private readonly IMercadoPagoCheckoutAdapter _mercadoPagoCheckoutAdapter;
    private readonly INotificationService _notificationService;

    public PaymentWebhookService(
        ApplicationDbContext dbContext,
        IEfUnitOfWork efUnitOfWork,
        IMercadoPagoCheckoutAdapter mercadoPagoCheckoutAdapter,
        INotificationService notificationService)
    {
        _dbContext = dbContext;
        _efUnitOfWork = efUnitOfWork;
        _mercadoPagoCheckoutAdapter = mercadoPagoCheckoutAdapter;
        _notificationService = notificationService;
    }

    public async Task<ProcessWebhookResultDto> ProcessMercadoPagoWebhookAsync(
        string rawPayload,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var transactionId = MercadoPagoWebhookPayloadParser.TryGetTransactionId(rawPayload);
        if (string.IsNullOrWhiteSpace(transactionId))
        {
            return new ProcessWebhookResultDto { Ignored = true };
        }

        var payment = await _dbContext.Payments
            .SingleOrDefaultAsync(x => x.Gateway == PaymentGateway.MercadoPago && x.GatewayTransactionId == transactionId, cancellationToken);

        Guid? storeId = null;
        if (payment is not null)
        {
            var orderStoreId = await _dbContext.Orders
                .AsNoTracking()
                .Where(x => x.Id == payment.OrderId)
                .Select(x => (Guid?)x.StoreId)
                .SingleOrDefaultAsync(cancellationToken);
            storeId = orderStoreId;
        }

        var gatewayDetails = await _mercadoPagoCheckoutAdapter.GetPaymentDetailsAsync(transactionId, storeId, cancellationToken);
        var mappedPaymentStatus = MapPaymentStatus(gatewayDetails.Status);
        var eventKey = $"{gatewayDetails.TransactionId}:{mappedPaymentStatus}";

        var alreadyProcessed = await _dbContext.PaymentWebhookEvents
            .AsNoTracking()
            .AnyAsync(x => x.Gateway == PaymentGateway.MercadoPago && x.EventKey == eventKey, cancellationToken);

        if (alreadyProcessed)
        {
            return new ProcessWebhookResultDto { Ignored = true };
        }

        await _dbContext.PaymentWebhookEvents.AddAsync(new PaymentWebhookEvent
        {
            Gateway = PaymentGateway.MercadoPago,
            EventKey = eventKey,
            GatewayTransactionId = gatewayDetails.TransactionId,
            Payload = rawPayload
        }, cancellationToken);

        if (payment is null)
        {
            await _dbContext.AuditLogs.AddAsync(new AuditLog
            {
                Event = "MercadoPagoWebhookPaymentNotFound",
                Entity = nameof(Payment),
                Description = $"Payment not found for transaction {gatewayDetails.TransactionId}.",
                IpAddress = ipAddress
            }, cancellationToken);

            await _efUnitOfWork.SaveChangesAsync(cancellationToken);
            return new ProcessWebhookResultDto { PaymentNotFound = true };
        }

        var previousPaymentStatus = payment.Status;
        payment.Status = mappedPaymentStatus;
        payment.RawPayload = gatewayDetails.RawPayload;
        payment.MarkAsUpdated();

        if (previousPaymentStatus != mappedPaymentStatus)
        {
            await _dbContext.PaymentStatusHistories.AddAsync(new PaymentStatusHistory
            {
                PaymentId = payment.Id,
                PreviousStatus = previousPaymentStatus,
                NewStatus = mappedPaymentStatus,
                Source = "Webhook",
                Notes = "Payment status changed from Mercado Pago webhook.",
                RawPayload = gatewayDetails.RawPayload
            }, cancellationToken);
        }

        var order = await _dbContext.Orders.SingleOrDefaultAsync(x => x.Id == payment.OrderId, cancellationToken);
        if (order is not null)
        {
            var storeOwnerUserId = await _dbContext.Stores
                .AsNoTracking()
                .Where(x => x.Id == order.StoreId)
                .Select(x => x.OwnerUserId)
                .SingleOrDefaultAsync(cancellationToken);

            var targetOrderStatus = MapOrderStatus(order.Status, mappedPaymentStatus);
            if (targetOrderStatus.HasValue && targetOrderStatus.Value != order.Status)
            {
                var previous = order.Status;
                order.Status = targetOrderStatus.Value;
                order.MarkAsUpdated();

                await _dbContext.OrderStatusHistories.AddAsync(new OrderStatusHistory
                {
                    OrderId = order.Id,
                    PreviousStatus = previous,
                    NewStatus = targetOrderStatus.Value,
                    ChangedByUserId = order.CustomerUserId,
                    Notes = "Order status updated from Mercado Pago webhook."
                }, cancellationToken);

                if (targetOrderStatus.Value == OrderStatus.Received && storeOwnerUserId != Guid.Empty)
                {
                    await _notificationService.NotifySellerNewOrderAsync(
                        storeOwnerUserId,
                        order.Id,
                        $"Novo pedido {order.Code} confirmado com pagamento online.",
                        cancellationToken);
                }

                await _notificationService.NotifyCustomerOrderStatusChangedAsync(
                    order.CustomerUserId,
                    order.Id,
                    targetOrderStatus.Value,
                    $"Seu pedido {order.Code} foi atualizado para {targetOrderStatus.Value}.",
                    cancellationToken);
            }
        }

        await _dbContext.AuditLogs.AddAsync(new AuditLog
        {
            UserId = order?.CustomerUserId,
            Event = "MercadoPagoWebhookProcessed",
            Entity = nameof(Payment),
            EntityId = payment.Id,
            Description = $"Webhook processed with status {mappedPaymentStatus} for transaction {gatewayDetails.TransactionId}.",
            IpAddress = ipAddress
        }, cancellationToken);

        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        return new ProcessWebhookResultDto
        {
            Processed = true
        };
    }

    private static PaymentStatus MapPaymentStatus(string externalStatus)
    {
        return externalStatus.Trim().ToLowerInvariant() switch
        {
            "approved" => PaymentStatus.Paid,
            "rejected" => PaymentStatus.Failed,
            "cancelled" => PaymentStatus.Cancelled,
            "refunded" => PaymentStatus.Refunded,
            _ => PaymentStatus.Pending
        };
    }

    private static OrderStatus? MapOrderStatus(OrderStatus current, PaymentStatus paymentStatus)
    {
        if (current != OrderStatus.PendingPayment)
        {
            return null;
        }

        return paymentStatus switch
        {
            PaymentStatus.Paid => OrderStatus.Received,
            PaymentStatus.Failed => OrderStatus.Cancelled,
            PaymentStatus.Cancelled => OrderStatus.Cancelled,
            PaymentStatus.Refunded => OrderStatus.Cancelled,
            _ => null
        };
    }
}
