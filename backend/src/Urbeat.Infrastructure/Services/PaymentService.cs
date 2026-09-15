using System.Collections.Concurrent;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Services.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Urbeat.Infrastructure.Services;

public sealed class PaymentService : IPaymentService
{
    /// <summary>
    /// Maximum number of additional attempts after the first loses the optimistic-concurrency race.
    /// A single retry is sufficient: the fresh attempt gets a future deadline that the mock Pix
    /// worker will not touch, so a second conflict cannot occur in normal operation.
    /// </summary>
    private const int MaxConcurrencyRetries = 1;

    private static readonly ConcurrentDictionary<Guid, OrderStartLock> OrderStartLocks = new();

    private readonly ApplicationDbContext _dbContext;
    private readonly IEfUnitOfWork _efUnitOfWork;
    private readonly IOrderPaymentStrategyFactory _strategyFactory;
    private readonly IMockPixClock _clock;

    public PaymentService(
        ApplicationDbContext dbContext,
        IEfUnitOfWork efUnitOfWork,
        IOrderPaymentStrategyFactory strategyFactory,
        IMockPixClock clock)
    {
        _dbContext = dbContext;
        _efUnitOfWork = efUnitOfWork;
        _strategyFactory = strategyFactory;
        _clock = clock;
    }

    public async Task<CreateOrderPaymentResultDto> CreateOrderPaymentAsync(
        Guid customerUserId,
        CreateOrderPaymentRequestDto request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        // The process-local lock serializes the common single-instance case while the PostgreSQL
        // advisory lock (taken below for relational providers) is the cross-process/replica source of
        // truth that guarantees a single gateway call per order.
        var orderLock = AcquireOrderStartLock(request.OrderId);
        var lockAcquired = false;
        try
        {
            await orderLock.Semaphore.WaitAsync(cancellationToken);
            lockAcquired = true;

            for (var attempt = 0; ; attempt++)
            {
                IDbContextTransaction? transaction = null;
                if (_dbContext.Database.IsRelational())
                {
                    transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
                    await AcquireOrderAdvisoryLockAsync(request.OrderId, cancellationToken);
                }

                try
                {
                    var result = await CreateOrderPaymentCoreAsync(customerUserId, request, ipAddress, cancellationToken);
                    if (transaction is not null)
                    {
                        await transaction.CommitAsync(cancellationToken);
                    }

                    return result;
                }
                catch (DbUpdateException ex) when (IsUniqueViolation(ex))
                {
                    // A concurrent request (possibly from another process) persisted the payment for this
                    // order first. The failed INSERT left the transaction aborted, so it must be rolled
                    // back and disposed before the winner can be read on the same connection — otherwise
                    // Npgsql reports "current transaction is aborted". The gateway is not called again.
                    await DisposeAbortedTransactionAsync(transaction, cancellationToken);
                    DetachStagedEntities();

                    var winner = await QueryWinnerPaymentAsync(request.OrderId, cancellationToken);
                    return new CreateOrderPaymentResultDto { Payment = ToResponse(winner) };
                }
                catch (DbUpdateConcurrencyException)
                {
                    // A worker (mock Pix) finalized the payment while this retry was starting a new
                    // attempt. Roll back, discard the staged entities, then re-read the committed
                    // state: when it is retryable (Failed/Cancelled, or a mock Pending whose window
                    // has elapsed) a fresh attempt is started idempotently; otherwise the committed
                    // state is returned as-is so NotFound/invalid-state semantics are not masked and
                    // the gateway is never invoked a second time.
                    await DisposeAbortedTransactionAsync(transaction, cancellationToken);
                    DetachStagedEntities();

                    var current = await QueryWinnerPaymentAsync(request.OrderId, cancellationToken);
                    if (attempt >= MaxConcurrencyRetries || !IsRetryableAfterConcurrentUpdate(current))
                    {
                        return new CreateOrderPaymentResultDto { Payment = ToResponse(current) };
                    }
                }
                finally
                {
                    if (transaction is not null)
                    {
                        await transaction.DisposeAsync();
                    }
                }
            }
        }
        finally
        {
            ReleaseOrderStartLock(request.OrderId, orderLock, lockAcquired);
        }
    }

    private static OrderStartLock AcquireOrderStartLock(Guid orderId)
    {
        while (true)
        {
            var entry = OrderStartLocks.GetOrAdd(orderId, static _ => new OrderStartLock());
            lock (entry)
            {
                if (OrderStartLocks.TryGetValue(orderId, out var registered) && ReferenceEquals(registered, entry))
                {
                    entry.Users++;
                    return entry;
                }
            }
        }
    }

    private static void ReleaseOrderStartLock(Guid orderId, OrderStartLock entry, bool lockAcquired)
    {
        if (lockAcquired)
        {
            entry.Semaphore.Release();
        }

        lock (entry)
        {
            if (--entry.Users == 0)
            {
                var removed = ((ICollection<KeyValuePair<Guid, OrderStartLock>>)OrderStartLocks)
                    .Remove(new KeyValuePair<Guid, OrderStartLock>(orderId, entry));
                if (removed)
                {
                    entry.Semaphore.Dispose();
                }
            }
        }
    }

    private sealed class OrderStartLock
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);

        public int Users;
    }

    private async Task<Payment> QueryWinnerPaymentAsync(Guid orderId, CancellationToken cancellationToken)
    {
        return await _dbContext.Payments
            .AsNoTracking()
            .SingleAsync(x => x.OrderId == orderId, cancellationToken);
    }

    private async Task AcquireOrderAdvisoryLockAsync(Guid orderId, CancellationToken cancellationToken)
    {
        // pg_advisory_xact_lock is transaction-scoped and released automatically at commit/rollback.
        // Two 32-bit keys derived from the order id form a 64-bit lock key, serializing payment
        // creation for the same order across concurrent requests and replicas. It is only valid on
        // PostgreSQL, so other relational providers (e.g. SQLite used in tests) skip it.
        if (_dbContext.Database.ProviderName != "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            return;
        }

        var bytes = orderId.ToByteArray();
        var key1 = BitConverter.ToInt32(bytes, 0);
        var key2 = BitConverter.ToInt32(bytes, 4);

        await _dbContext.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock({0}, {1})",
            cancellationToken,
            key1,
            key2);
    }

    private async Task<CreateOrderPaymentResultDto> CreateOrderPaymentCoreAsync(
        Guid customerUserId,
        CreateOrderPaymentRequestDto request,
        string? ipAddress,
        CancellationToken cancellationToken)
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

        return new CreateOrderPaymentResultDto { Payment = payment };
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
            ExpiresAtUtc = payment.Gateway == PaymentGateway.Mock ? payment.MockExpiresAtUtc : null,
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

    private void DetachStagedEntities()
    {
        foreach (var entry in _dbContext.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified)
            .ToArray())
        {
            entry.State = EntityState.Detached;
        }
    }

    private static async Task DisposeAbortedTransactionAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is null)
        {
            return;
        }

        await transaction.RollbackAsync(cancellationToken);
        await transaction.DisposeAsync();
    }

    private bool IsRetryableAfterConcurrentUpdate(Payment payment)
    {
        // Only mock Pix payments may be retried automatically after losing the optimistic-concurrency
        // race. A real gateway (Mercado Pago) must never be re-executed here: repeating the strategy
        // would re-invoke the adapter with no idempotency guarantee and could double-charge/duplicate
        // the checkout.
        if (payment.Gateway != PaymentGateway.Mock)
        {
            return false;
        }

        if (payment.Status is PaymentStatus.Failed or PaymentStatus.Cancelled)
        {
            return true;
        }

        return payment.Status == PaymentStatus.Pending
            && payment.MockExpiresAtUtc.HasValue
            && payment.MockExpiresAtUtc <= _clock.UtcNow;
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static OrderPaymentResponseDto ToResponse(Payment payment)
    {
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
            ExpiresAtUtc = payment.Gateway == PaymentGateway.Mock ? payment.MockExpiresAtUtc : null
        };
    }
}
