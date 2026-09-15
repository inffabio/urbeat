using System.Text.Json;
using Urbeat.Application.DTOs;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace Urbeat.Infrastructure.Services.Payments;

/// <summary>
/// Deterministic Pix simulation selected only when <c>Payments:Provider=Mock</c>. It never calls
/// Mercado Pago. Each fresh attempt chooses an outcome once (approved after a delay inside the
/// approval window, or expired at the end of the window) and persists the resulting timestamps so
/// a hosted worker can advance the payment and order state later.
/// </summary>
public sealed class MockPixPaymentStrategy : IOrderPaymentStrategy
{
    /// <summary>Internal route understood by the test UI. It is not a real payment credential.</summary>
    public const string MockCheckoutUrl = "/mock-pix-checkout";

    private readonly ApplicationDbContext _dbContext;
    private readonly MockPixOptions _options;
    private readonly IMockPixClock _clock;
    private readonly IMockPixRandom _random;

    public MockPixPaymentStrategy(
        ApplicationDbContext dbContext,
        IOptions<MockPixOptions> options,
        IMockPixClock clock,
        IMockPixRandom random)
    {
        _dbContext = dbContext;
        _options = options.Value;
        _clock = clock;
        _random = random;
    }

    public bool CanHandle(PaymentMethod method)
    {
        return _options.IsMockEnabled && method == PaymentMethod.PixOnline;
    }

    public Task<OrderPaymentResponseDto> StartAsync(
        Order order,
        Payment? existingPayment,
        CancellationToken cancellationToken = default)
    {
        // Terminal states are never reusable: a paid or refunded payment must not regress to Pending.
        if (existingPayment is not null && existingPayment.Status is PaymentStatus.Paid or PaymentStatus.Refunded)
        {
            return Task.FromResult(ToResponse(existingPayment));
        }

        var now = _clock.UtcNow;

        // Idempotency: a pending mock attempt is reused without re-rolling the outcome or starting a
        // new attempt while it is still inside its window. A pending attempt whose window has already
        // elapsed is treated as terminal so the retry starts a fresh attempt with a new outcome and
        // deadline instead of reusing an expired one.
        if (existingPayment is not null && IsReusable(existingPayment, now))
        {
            return Task.FromResult(ToResponse(existingPayment));
        }

        var expiredPending = existingPayment is { Status: PaymentStatus.Pending } && IsExpired(existingPayment, now);
        var retryingTerminal = existingPayment is { Status: PaymentStatus.Failed or PaymentStatus.Cancelled } || expiredPending;
        var previousStatus = existingPayment?.Status;
        var previousAttempt = existingPayment?.Attempt ?? 0;
        var previousRawPayload = existingPayment?.RawPayload;
        var attempt = retryingTerminal
            ? existingPayment!.Attempt + 1
            : existingPayment?.Attempt ?? 1;

        var expiresAtUtc = now.AddSeconds(_options.WindowSeconds);

        var outcome = _random.Next(0, 2) == 0 ? MockPixOutcome.Approved : MockPixOutcome.Expired;
        var approvalAtUtc = outcome == MockPixOutcome.Approved
            ? now.AddSeconds(_random.Next(_options.ApprovalMinimumSeconds, _options.ApprovalMaximumSeconds + 1))
            : (DateTime?)null;

        var payment = existingPayment ?? new Payment
        {
            OrderId = order.Id,
            Gateway = PaymentGateway.Mock,
            Method = order.PaymentMethod,
            Amount = order.Total,
            Status = PaymentStatus.Pending
        };

        payment.Gateway = PaymentGateway.Mock;
        payment.Method = order.PaymentMethod;
        payment.Amount = order.Total;
        payment.Status = PaymentStatus.Pending;
        payment.Attempt = attempt;
        // A fresh attempt must invalidate any worker iteration still holding the previous entity
        // snapshot. Regenerating the optimistic concurrency token guarantees that a worker carrying
        // the stale stamp cannot finalize this new attempt and clobber its outcome.
        payment.ConcurrencyStamp = Guid.CreateVersion7();
        payment.ExternalReference = order.Id.ToString();
        payment.GatewayTransactionId = $"mock_{order.Id:N}_{attempt}";
        payment.GatewayCheckoutUrl = MockCheckoutUrl;
        payment.MockExpiresAtUtc = expiresAtUtc;
        payment.MockApprovalAtUtc = approvalAtUtc;
        payment.MockOutcome = outcome.ToString();
        payment.RawPayload = JsonSerializer.Serialize(new
        {
            provider = "Mock",
            outcome = outcome.ToString(),
            expiresAtUtc,
            approvalAtUtc
        });
        payment.MarkAsUpdated();

        if (existingPayment is null)
        {
            _dbContext.Payments.Add(payment);
        }

        if (expiredPending)
        {
            // A pending attempt whose window already elapsed is terminal. Record its expiration as a
            // Failed transition (with audit) before starting the fresh Pending attempt, so the state
            // machine and history stay accurate instead of a bare Pending -> Pending entry.
            _dbContext.PaymentStatusHistories.Add(new PaymentStatusHistory
            {
                PaymentId = payment.Id,
                PreviousStatus = PaymentStatus.Pending,
                NewStatus = PaymentStatus.Failed,
                Source = "Mock",
                Notes = "Mock Pix payment expired before approval.",
                RawPayload = previousRawPayload
            });

            _dbContext.AuditLogs.Add(new AuditLog
            {
                UserId = order.CustomerUserId,
                Event = "MockPixPaymentExpired",
                Entity = nameof(Payment),
                EntityId = payment.Id,
                Description = $"Mock Pix attempt {previousAttempt} expired before approval."
            });
        }

        if (retryingTerminal)
        {
            _dbContext.PaymentStatusHistories.Add(new PaymentStatusHistory
            {
                PaymentId = payment.Id,
                PreviousStatus = expiredPending ? PaymentStatus.Failed : previousStatus,
                NewStatus = PaymentStatus.Pending,
                Source = "Checkout",
                Notes = "Payment retried with a new mock Pix attempt.",
                RawPayload = payment.RawPayload
            });
        }

        _dbContext.AuditLogs.Add(new AuditLog
        {
            UserId = order.CustomerUserId,
            Event = "MockPixPaymentStarted",
            Entity = nameof(Payment),
            EntityId = payment.Id,
            Description = JsonSerializer.Serialize(new
            {
                orderId = order.Id,
                payment.Status,
                payment.GatewayTransactionId,
                expiresAtUtc,
                approvalAtUtc,
                outcome = outcome.ToString()
            })
        });

        return Task.FromResult(ToResponse(payment));
    }

    private static bool IsReusable(Payment payment, DateTime now) =>
        payment.Status == PaymentStatus.Pending
        && payment.Gateway == PaymentGateway.Mock
        && !string.IsNullOrWhiteSpace(payment.GatewayTransactionId)
        && payment.MockExpiresAtUtc.HasValue
        && payment.MockExpiresAtUtc > now;

    private static bool IsExpired(Payment payment, DateTime now) =>
        payment.MockExpiresAtUtc.HasValue && payment.MockExpiresAtUtc <= now;

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
            ExpiresAtUtc = payment.MockExpiresAtUtc
        };
    }
}
