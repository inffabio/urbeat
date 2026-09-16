using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Outbox;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Services.Payments;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Urbeat.Infrastructure.Services;

public sealed class PaymentWebhookService : IPaymentWebhookService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IEfUnitOfWork _efUnitOfWork;
    private readonly IMercadoPagoCheckoutAdapter _mercadoPagoCheckoutAdapter;
    private readonly INotificationService _notificationService;
    private readonly IOutboxWriter _outboxWriter;

    public PaymentWebhookService(
        ApplicationDbContext dbContext,
        IEfUnitOfWork efUnitOfWork,
        IMercadoPagoCheckoutAdapter mercadoPagoCheckoutAdapter,
        INotificationService notificationService,
        IOutboxWriter outboxWriter)
    {
        _dbContext = dbContext;
        _efUnitOfWork = efUnitOfWork;
        _mercadoPagoCheckoutAdapter = mercadoPagoCheckoutAdapter;
        _notificationService = notificationService;
        _outboxWriter = outboxWriter;
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

        try
        {
            return await ProcessWebhookCoreAsync(
                rawPayload,
                ipAddress,
                payment,
                gatewayDetails,
                mappedPaymentStatus,
                eventKey,
                cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicatePaymentWebhookEvent(exception))
        {
            // A concurrent webhook persisted the same (Gateway, EventKey) first. The unique
            // constraint is the real guard; treat the losing request as an idempotent duplicate
            // instead of failing.
            return new ProcessWebhookResultDto { Ignored = true };
        }
    }

    private async Task<ProcessWebhookResultDto> ProcessWebhookCoreAsync(
        string rawPayload,
        string? ipAddress,
        Payment? payment,
        MercadoPagoPaymentDetails gatewayDetails,
        PaymentStatus mappedPaymentStatus,
        string eventKey,
        CancellationToken cancellationToken)
    {
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

        // The persisted order total is authoritative. A paid webhook is only accepted when the real
        // gateway reports the same amount and a BRL currency. Missing amount/currency is treated as a
        // mismatch so an external payload that omits the financial data can never mark a payment as
        // paid. Only the explicitly simulated local fake/mock gateway (IsSimulated, set in code by
        // the adapter and never read from the external payload) may omit them.
        var authoritativeTotal = order?.Total ?? payment.Amount;
        var amountMatches = gatewayDetails.Amount is not null
            && gatewayDetails.Amount.Value == authoritativeTotal;
        var currencyMatches = !string.IsNullOrWhiteSpace(gatewayDetails.CurrencyId)
            && string.Equals(gatewayDetails.CurrencyId, "BRL", StringComparison.OrdinalIgnoreCase);
        var gatewayAmountAllowsPaid = mappedPaymentStatus != PaymentStatus.Paid
            || gatewayDetails.IsSimulated
            || (amountMatches && currencyMatches);

        var statusChanged = previousPaymentStatus != mappedPaymentStatus
            && canTransition
            && isCurrentAttempt
            && orderAllowsPaid
            && gatewayAmountAllowsPaid;

        if (statusChanged)
        {
            payment.Status = mappedPaymentStatus;
            payment.RawPayload = gatewayDetails.RawPayload;
            payment.ConcurrencyStamp = Guid.CreateVersion7();
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
                    order.StatusVersion += 1;
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

                    await _outboxWriter.EnqueueAsync(
                        OutboxEventTypes.OrderStatusChanged,
                        order.Id,
                        new OrderStatusChangedEvent
                        {
                            OrderId = order.Id,
                            StoreId = order.StoreId,
                            CustomerUserId = order.CustomerUserId,
                            SellerUserId = storeOwnerUserId,
                            Code = order.Code,
                            PreviousStatus = previous,
                            NewStatus = targetOrderStatus.Value,
                            ChangedAtUtc = DateTime.UtcNow,
                            Source = "Webhook",
                            Sequence = order.StatusVersion
                        },
                        DateTime.UtcNow,
                        sequence: order.StatusVersion,
                        aggregateType: nameof(Order),
                        cancellationToken);
                }
            }
        }

        await _dbContext.AuditLogs.AddAsync(new AuditLog
        {
            UserId = order?.CustomerUserId,
            Event = statusChanged
                ? "MercadoPagoWebhookProcessed"
                : !gatewayAmountAllowsPaid
                    ? "MercadoPagoWebhookAmountMismatch"
                    : "MercadoPagoWebhookTerminalStateProtected",
            Entity = nameof(Payment),
            EntityId = payment.Id,
            Description = statusChanged
                ? $"Webhook processed with status {mappedPaymentStatus} for transaction {gatewayDetails.TransactionId} (attempt {payment.Attempt})."
                : DescribeIgnoredWebhook(previousPaymentStatus, mappedPaymentStatus, canTransition, isCurrentAttempt, order?.Status, gatewayAmountAllowsPaid, gatewayDetails.Amount, authoritativeTotal, gatewayDetails.CurrencyId),
            IpAddress = ipAddress
        }, cancellationToken);

        try
        {
            await _efUnitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Two webhooks with different statuses raced on the same payment. The winning request
            // already committed the transition and its history; this losing write must not overwrite
            // it. Re-read the committed state, record only the idempotency event key, and report the
            // webhook as ignored so the state machine and history stay consistent.
            return await ReconcileConcurrentWebhookAsync(
                rawPayload,
                ipAddress,
                payment,
                gatewayDetails,
                mappedPaymentStatus,
                eventKey,
                cancellationToken);
        }

        return new ProcessWebhookResultDto
        {
            Processed = true
        };
    }

    private async Task<ProcessWebhookResultDto> ReconcileConcurrentWebhookAsync(
        string rawPayload,
        string? ipAddress,
        Payment payment,
        MercadoPagoPaymentDetails gatewayDetails,
        PaymentStatus mappedPaymentStatus,
        string eventKey,
        CancellationToken cancellationToken)
    {
        // Discard the stale tracked entities so the committed state can be re-read instead of
        // retrying the losing write.
        foreach (var entry in _dbContext.ChangeTracker.Entries().ToList())
        {
            entry.State = EntityState.Detached;
        }

        var currentPayment = await _dbContext.Payments
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == payment.Id, cancellationToken);

        await _dbContext.PaymentWebhookEvents.AddAsync(new PaymentWebhookEvent
        {
            Gateway = PaymentGateway.MercadoPago,
            EventKey = eventKey,
            GatewayTransactionId = gatewayDetails.TransactionId,
            Payload = rawPayload
        }, cancellationToken);

        await _dbContext.AuditLogs.AddAsync(new AuditLog
        {
            Event = "MercadoPagoWebhookConcurrentConflict",
            Entity = nameof(Payment),
            EntityId = payment.Id,
            Description = $"Webhook for transaction {gatewayDetails.TransactionId} lost a concurrent transition " +
                          $"to {mappedPaymentStatus}; payment is now {currentPayment?.Status}.",
            IpAddress = ipAddress
        }, cancellationToken);

        try
        {
            await _efUnitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicatePaymentWebhookEvent(exception))
        {
            // Another request already recorded this event key; still an idempotent conflict.
        }

        return new ProcessWebhookResultDto { Ignored = true };
    }

    private static bool IsDuplicatePaymentWebhookEvent(DbUpdateException exception)
    {
        // PostgreSQL raises SQLSTATE 23505 when a racing request persists the same
        // (Gateway, EventKey). Only the PaymentWebhookEvents constraint maps to an idempotent
        // duplicate; any other DbUpdateException is left to propagate.
        return exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        } pg && pg.MessageText.Contains("PaymentWebhookEvents", StringComparison.OrdinalIgnoreCase);
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
        OrderStatus? orderStatus,
        bool gatewayAmountAllowsPaid,
        decimal? gatewayAmount,
        decimal authoritativeTotal,
        string? gatewayCurrency)
    {
        if (!isCurrentAttempt)
        {
            return $"Webhook ignored: gateway transaction does not match the payment's current attempt (payment {previous}, incoming {incoming}).";
        }

        if (!canTransition)
        {
            return $"Webhook ignored: transition {previous} -> {incoming} is not allowed.";
        }

        if (!gatewayAmountAllowsPaid)
        {
            return $"Webhook ignored: gateway reported amount {gatewayAmount?.ToString() ?? "unknown"} / currency {gatewayCurrency ?? "unknown"} which does not match the persisted order total {authoritativeTotal}.";
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
