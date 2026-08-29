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
        var canTransition = CanTransition(previousPaymentStatus, mappedPaymentStatus);
        var isCurrentAttempt = string.Equals(payment.GatewayTransactionId, gatewayDetails.TransactionId, StringComparison.Ordinal);

        var order = await _dbContext.Orders.SingleOrDefaultAsync(x => x.Id == payment.OrderId, cancellationToken);
        var orderAllowsPaid = mappedPaymentStatus != PaymentStatus.Paid
            || (order is not null && CanMarkPaid(order.Status));

        var statusChanged = previousPaymentStatus != mappedPaymentStatus
            && canTransition
            && isCurrentAttempt
            && orderAllowsPaid;

        if (statusChanged)
        {
            payment.Status = mappedPaymentStatus;
            payment.RawPayload = gatewayDetails.RawPayload;
            payment.MarkAsUpdated();

            await _dbContext.PaymentStatusHistories.AddAsync(new PaymentStatusHistory
            {
                PaymentId = payment.Id,
                PreviousStatus = previousPaymentStatus,
                NewStatus = mappedPaymentStatus,
                Source = "Webhook",
                Notes = "Payment status changed from Mercado Pago webhook.",
                RawPayload = gatewayDetails.RawPayload
            }, cancellationToken);

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
        }

        await _dbContext.AuditLogs.AddAsync(new AuditLog
        {
            UserId = order?.CustomerUserId,
            Event = statusChanged ? "MercadoPagoWebhookProcessed" : "MercadoPagoWebhookTerminalStateProtected",
            Entity = nameof(Payment),
            EntityId = payment.Id,
            Description = statusChanged
                ? $"Webhook processed with status {mappedPaymentStatus} for transaction {gatewayDetails.TransactionId} (attempt {payment.Attempt})."
                : DescribeIgnoredWebhook(previousPaymentStatus, mappedPaymentStatus, canTransition, isCurrentAttempt, order?.Status),
            IpAddress = ipAddress
        }, cancellationToken);

        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        return new ProcessWebhookResultDto
        {
            Processed = true
        };
    }

    /// <summary>
    /// Defines the allowed payment status transitions as an explicit allow-list. Only a pending
    /// payment may advance (to Paid, Failed or Cancelled) and only a paid payment may later be
    /// refunded. Any other transition — including a late "approved" that would resurrect a failed,
    /// cancelled or refunded payment — is ignored so the payment and its order cannot diverge.
    /// </summary>
    private static bool CanTransition(PaymentStatus current, PaymentStatus incoming)
    {
        return (current, incoming) switch
        {
            (PaymentStatus.Pending, PaymentStatus.Paid) => true,
            (PaymentStatus.Pending, PaymentStatus.Failed) => true,
            (PaymentStatus.Pending, PaymentStatus.Cancelled) => true,
            (PaymentStatus.Paid, PaymentStatus.Refunded) => true,
            _ => false
        };
    }

    /// <summary>
    /// A late "approved" webhook must not mark a payment as Paid once the order has already been
    /// cancelled or delivered, since doing so would leave the payment and order inconsistent.
    /// </summary>
    private static bool CanMarkPaid(OrderStatus orderStatus)
    {
        return orderStatus is not OrderStatus.Cancelled and not OrderStatus.Delivered;
    }

    private static string DescribeIgnoredWebhook(
        PaymentStatus previous,
        PaymentStatus incoming,
        bool canTransition,
        bool isCurrentAttempt,
        OrderStatus? orderStatus)
    {
        if (!isCurrentAttempt)
        {
            return $"Webhook ignored: gateway transaction does not match the payment's current attempt (payment {previous}, incoming {incoming}).";
        }

        if (!canTransition)
        {
            return $"Webhook ignored: transition {previous} -> {incoming} is not allowed.";
        }

        return $"Webhook ignored: order status {orderStatus} cannot receive a paid payment from a late {incoming} webhook.";
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

        // A failed or cancelled payment must leave the order in PendingPayment so the customer can
        // safely retry payment. Only a paid payment advances the order to Received; terminal order
        // states (Cancelled/Delivered) are guarded separately by CanMarkPaid.
        return paymentStatus switch
        {
            PaymentStatus.Paid => OrderStatus.Received,
            _ => null
        };
    }
}
