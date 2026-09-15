using System.Text.Json;
using Urbeat.Application.DTOs;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.Infrastructure.Services.Payments;

public sealed class MercadoPagoOrderPaymentStrategy : IOrderPaymentStrategy
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IMercadoPagoCheckoutAdapter _mercadoPagoCheckoutAdapter;

    public MercadoPagoOrderPaymentStrategy(
        ApplicationDbContext dbContext,
        IMercadoPagoCheckoutAdapter mercadoPagoCheckoutAdapter)
    {
        _dbContext = dbContext;
        _mercadoPagoCheckoutAdapter = mercadoPagoCheckoutAdapter;
    }

    public bool CanHandle(PaymentMethod method)
    {
        return method is PaymentMethod.PixOnline or PaymentMethod.CardOnline;
    }


    public async Task<OrderPaymentResponseDto> StartAsync(
        Order order,
        Payment? existingPayment,
        CancellationToken cancellationToken = default)
    {
        // Terminal states are never reusable: a paid or refunded payment must not regress back to
        // Pending, so return it as-is without contacting the gateway.
        if (existingPayment is not null && existingPayment.Status is PaymentStatus.Paid or PaymentStatus.Refunded)
        {
            return ToResponse(existingPayment);
        }

        // Idempotency: payment is unique per order. When a reusable checkout has already been
        // created for this order (a pending gateway transaction), reuse it instead of starting a
        // new one. Failed/cancelled payments allow a safe retry that reuses the same Payment row
        // (the order id is unique) under a fresh idempotency key.
        if (existingPayment is not null && IsReusable(existingPayment))
        {
            return ToResponse(existingPayment);
        }

        var attempt = existingPayment is { Status: PaymentStatus.Failed or PaymentStatus.Cancelled }
            ? existingPayment.Attempt + 1
            : existingPayment?.Attempt ?? 1;

        var customerEmail = await _dbContext.Users
            .AsNoTracking()
            .Where(x => x.Id == order.CustomerUserId)
            .Select(x => x.Email)
            .SingleOrDefaultAsync(cancellationToken) ?? "cliente@urbeat.local";

        var items = await _dbContext.OrderItems
            .AsNoTracking()
            .Where(x => x.OrderId == order.Id)
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => new MercadoPagoCheckoutItem
            {
                Title = x.ProductName,
                Quantity = x.Quantity,
                UnitPrice = x.UnitPrice
            })
            .ToListAsync(cancellationToken);

        var gatewayCheckout = await _mercadoPagoCheckoutAdapter.CreateCheckoutAsync(new MercadoPagoCheckoutCreateRequest
        {
            ExternalReference = order.Id.ToString(),
            PayerEmail = customerEmail,
            Items = items,
            IdempotencyKey = $"{order.Id:N}:{attempt}"
        }, order.StoreId, cancellationToken);

        var payment = existingPayment ?? new Payment
        {
            OrderId = order.Id,
            Gateway = PaymentGateway.MercadoPago,
            Method = order.PaymentMethod,
            Amount = order.Total,
            Status = PaymentStatus.Pending
        };

        payment.Gateway = PaymentGateway.MercadoPago;
        payment.Method = order.PaymentMethod;
        payment.Amount = order.Total;
        payment.Status = PaymentStatus.Pending;
        payment.Attempt = attempt;
        payment.ExternalReference = order.Id.ToString();
        payment.GatewayTransactionId = gatewayCheckout.TransactionId;
        payment.GatewayCheckoutUrl = gatewayCheckout.CheckoutUrl;
        payment.RawPayload = gatewayCheckout.RawPayload;
        // A reused Payment row must never leak mock Pix simulation metadata into a real gateway
        // attempt (e.g. when the provider configuration changed or a retry reuses the same row).
        payment.MockExpiresAtUtc = null;
        payment.MockApprovalAtUtc = null;
        payment.MockOutcome = null;
        payment.MarkAsUpdated();

        if (existingPayment is null)
        {
            await _dbContext.Payments.AddAsync(payment, cancellationToken);
        }

        await _dbContext.AuditLogs.AddAsync(new AuditLog
        {
            UserId = order.CustomerUserId,
            Event = "PaymentStarted",
            Entity = nameof(Payment),
            EntityId = payment.Id,
            Description = JsonSerializer.Serialize(new
            {
                orderId = order.Id,
                gateway = payment.Gateway,
                payment.Status,
                payment.GatewayTransactionId
            })
        }, cancellationToken);

        return ToResponse(payment);
    }

    private static bool IsReusable(Payment payment) =>
        payment.Status == PaymentStatus.Pending
        && !string.IsNullOrWhiteSpace(payment.GatewayTransactionId);

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
            UpdatedAtUtc = payment.UpdatedAtUtc
        };
    }
}
