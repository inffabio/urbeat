using System.Text.Json;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Outbox;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.Infrastructure.Services.Payments;

/// <summary>
/// Applies the terminal transition for due mock Pix payments. Approval advances the payment to
/// <see cref="PaymentStatus.Paid"/> and the order to <see cref="OrderStatus.Received"/> using the
/// same rules as the real webhook path; expiration marks the payment
/// <see cref="PaymentStatus.Failed"/> and leaves the order in <see cref="OrderStatus.PendingPayment"/>
/// for retry. Every transition is written in a single unit of work and guarded by the payment's
/// optimistic concurrency token so a second worker iteration can never apply it twice.
/// </summary>
public sealed class MockPixPaymentProcessor
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IEfUnitOfWork _efUnitOfWork;
    private readonly INotificationService _notificationService;
    private readonly IOutboxWriter _outboxWriter;
    private readonly IMockPixClock _clock;

    public MockPixPaymentProcessor(
        ApplicationDbContext dbContext,
        IEfUnitOfWork efUnitOfWork,
        INotificationService notificationService,
        IOutboxWriter outboxWriter,
        IMockPixClock clock)
    {
        _dbContext = dbContext;
        _efUnitOfWork = efUnitOfWork;
        _notificationService = notificationService;
        _outboxWriter = outboxWriter;
        _clock = clock;
    }

    public async Task<int> ProcessDuePaymentsAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;

        var duePayments = await _dbContext.Payments
            .Where(x => x.Gateway == PaymentGateway.Mock
                && x.Method == PaymentMethod.PixOnline
                && x.Status == PaymentStatus.Pending
                && ((x.MockApprovalAtUtc != null && x.MockApprovalAtUtc <= now)
                    || (x.MockExpiresAtUtc != null && x.MockExpiresAtUtc <= now)))
            .ToListAsync(cancellationToken);

        var processed = 0;
        foreach (var payment in duePayments)
        {
            var outcome = Enum.TryParse<MockPixOutcome>(payment.MockOutcome, out var parsed)
                ? parsed
                : MockPixOutcome.Expired;

            var applied = outcome == MockPixOutcome.Approved
                ? await ApplyApprovalAsync(payment, now, cancellationToken)
                : await ApplyFailureAsync(payment, "Mock Pix payment expired before approval.", "MockPixPaymentExpired", "expired", cancellationToken);

            if (applied)
            {
                processed++;
            }
        }

        return processed;
    }

    private async Task<bool> ApplyApprovalAsync(Payment payment, DateTime now, CancellationToken cancellationToken)
    {
        var order = await _dbContext.Orders.SingleOrDefaultAsync(x => x.Id == payment.OrderId, cancellationToken);

        // A late approval must not mark the payment Paid once the order can no longer be paid,
        // mirroring the webhook's CanMarkPaid guard. Resolve it as a terminal failure instead so the
        // payment and order never diverge and the worker does not re-process it every iteration.
        if (order is not null && !CanMarkPaid(order.Status))
        {
            return await ApplyFailureAsync(
                payment,
                "Mock Pix approval blocked because the order can no longer be paid.",
                "MockPixPaymentApprovalBlocked",
                "blocked",
                cancellationToken);
        }

        var previousStatus = payment.Status;
        payment.Status = PaymentStatus.Paid;
        payment.ConcurrencyStamp = Guid.CreateVersion7();
        payment.RawPayload = MockPayload("approved");
        payment.MarkAsUpdated();

        var paymentHistory = new PaymentStatusHistory
        {
            PaymentId = payment.Id,
            PreviousStatus = previousStatus,
            NewStatus = PaymentStatus.Paid,
            Source = "Mock",
            Notes = "Mock Pix payment approved by the simulation worker.",
            RawPayload = payment.RawPayload
        };
        _dbContext.PaymentStatusHistories.Add(paymentHistory);

        OrderStatusHistory? orderHistory = null;
        if (order is not null && order.Status == OrderStatus.PendingPayment)
        {
            var storeOwnerUserId = await _dbContext.Stores
                .AsNoTracking()
                .Where(x => x.Id == order.StoreId)
                .Select(x => x.OwnerUserId)
                .SingleOrDefaultAsync(cancellationToken);

            var previousOrderStatus = order.Status;
            order.Status = OrderStatus.Received;
            order.StatusVersion += 1;
            order.MarkAsUpdated();

            orderHistory = new OrderStatusHistory
            {
                OrderId = order.Id,
                PreviousStatus = previousOrderStatus,
                NewStatus = OrderStatus.Received,
                ChangedByUserId = order.CustomerUserId,
                Notes = "Order status updated by mock Pix payment approval."
            };
            _dbContext.OrderStatusHistories.Add(orderHistory);

            if (storeOwnerUserId != Guid.Empty)
            {
                await _notificationService.NotifySellerNewOrderAsync(
                    storeOwnerUserId,
                    order.Id,
                    $"Novo pedido {order.Code} confirmado com pagamento (mock).",
                    cancellationToken);
            }

            await _notificationService.NotifyCustomerOrderStatusChangedAsync(
                order.CustomerUserId,
                order.Id,
                OrderStatus.Received,
                $"Seu pedido {order.Code} foi atualizado para {OrderStatus.Received}.",
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
                    PreviousStatus = previousOrderStatus,
                    NewStatus = OrderStatus.Received,
                    ChangedAtUtc = now,
                    Source = "Mock",
                    Sequence = order.StatusVersion
                },
                now,
                sequence: order.StatusVersion,
                aggregateType: nameof(Order),
                cancellationToken);
        }

        var auditLog = new AuditLog
        {
            UserId = order?.CustomerUserId,
            Event = "MockPixPaymentApproved",
            Entity = nameof(Payment),
            EntityId = payment.Id,
            Description = $"Mock Pix payment approved for attempt {payment.Attempt}."
        };
        await _dbContext.AuditLogs.AddAsync(auditLog, cancellationToken);

        return await SaveTransitionAsync(payment, order, paymentHistory, orderHistory, auditLog, cancellationToken);
    }

    private async Task<bool> ApplyFailureAsync(
        Payment payment,
        string notes,
        string auditEvent,
        string rawState,
        CancellationToken cancellationToken)
    {
        var previousStatus = payment.Status;
        payment.Status = PaymentStatus.Failed;
        payment.ConcurrencyStamp = Guid.CreateVersion7();
        payment.RawPayload = MockPayload(rawState);
        payment.MarkAsUpdated();

        var paymentHistory = new PaymentStatusHistory
        {
            PaymentId = payment.Id,
            PreviousStatus = previousStatus,
            NewStatus = PaymentStatus.Failed,
            Source = "Mock",
            Notes = notes,
            RawPayload = payment.RawPayload
        };
        _dbContext.PaymentStatusHistories.Add(paymentHistory);

        var auditLog = new AuditLog
        {
            Event = auditEvent,
            Entity = nameof(Payment),
            EntityId = payment.Id,
            Description = $"{notes} (attempt {payment.Attempt})."
        };
        await _dbContext.AuditLogs.AddAsync(auditLog, cancellationToken);

        return await SaveTransitionAsync(payment, null, paymentHistory, null, auditLog, cancellationToken);
    }

    private static bool CanMarkPaid(OrderStatus orderStatus) =>
        orderStatus is not OrderStatus.Cancelled and not OrderStatus.Delivered;

    private async Task<bool> SaveTransitionAsync(
        Payment payment,
        Order? order,
        PaymentStatusHistory paymentHistory,
        OrderStatusHistory? orderHistory,
        AuditLog auditLog,
        CancellationToken cancellationToken)
    {
        try
        {
            await _efUnitOfWork.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another worker iteration (or process) already committed this transition. Discard every
            // entity staged for this payment/transition — including the durable Notifications and
            // OutboxMessages queued during approval — so the state machine, history and side effects
            // written by the winner are preserved and the audit save below never persists a partial or
            // duplicated effect. Entities of other payments still pending in the batch remain
            // Unchanged and are therefore left untouched.
            DetachStagedEntities();

            await _dbContext.AuditLogs.AddAsync(new AuditLog
            {
                Event = "MockPixPaymentConcurrentConflict",
                Entity = nameof(Payment),
                EntityId = payment.Id,
                Description = "Mock Pix worker lost a concurrent transition; the payment was already terminal."
            }, cancellationToken);

            try
            {
                await _efUnitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another reconciliation is racing; the transition is already terminal and idempotent.
            }

            return false;
        }
    }

    private void DetachStagedEntities()
    {
        foreach (var entry in _dbContext.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified)
            .ToArray())
        {
            entry.State = EntityState.Detached;
        }
    }

    private static string MockPayload(string state) =>
        JsonSerializer.Serialize(new { provider = "Mock", state });
}
