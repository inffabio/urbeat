using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.Infrastructure.Services;

public sealed class SubscriptionWebhookService : ISubscriptionWebhookService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IEfUnitOfWork _efUnitOfWork;
    private readonly ISubscriptionNotificationService _subscriptionNotificationService;

    public SubscriptionWebhookService(
        ApplicationDbContext dbContext,
        IEfUnitOfWork efUnitOfWork,
        ISubscriptionNotificationService subscriptionNotificationService)
    {
        _dbContext = dbContext;
        _efUnitOfWork = efUnitOfWork;
        _subscriptionNotificationService = subscriptionNotificationService;
    }

    public async Task<ProcessWebhookResultDto> ProcessAsaasWebhookAsync(
        string rawPayload,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        AsaasWebhookPayloadParser.ParsedAsaasWebhook? parsed;
        try
        {
            parsed = AsaasWebhookPayloadParser.TryParse(rawPayload);
        }
        catch
        {
            return new ProcessWebhookResultDto { Ignored = true };
        }

        if (parsed is null)
        {
            return new ProcessWebhookResultDto { Ignored = true };
        }

        var alreadyProcessed = await _dbContext.SubscriptionWebhookEvents
            .AsNoTracking()
            .AnyAsync(x => x.EventKey == parsed.EventKey, cancellationToken);

        if (alreadyProcessed)
        {
            return new ProcessWebhookResultDto { Ignored = true };
        }

        await _dbContext.SubscriptionWebhookEvents.AddAsync(new SubscriptionWebhookEvent
        {
            EventKey = parsed.EventKey,
            EventType = parsed.EventType,
            SellerUserId = parsed.SellerUserId,
            ExternalReference = parsed.ExternalReference,
            Payload = rawPayload
        }, cancellationToken);

        if (parsed.SellerUserId is null)
        {
            await _dbContext.AuditLogs.AddAsync(new AuditLog
            {
                Event = "AsaasWebhookIgnoredMissingSeller",
                Entity = nameof(SellerSubscriptionStatus),
                Description = $"Asaas webhook ignored because seller could not be resolved. EventKey: {parsed.EventKey}.",
                IpAddress = ipAddress
            }, cancellationToken);

            await _efUnitOfWork.SaveChangesAsync(cancellationToken);

            return new ProcessWebhookResultDto { Ignored = true };
        }


        var mappedBillingStatus = MapBillingStatus(parsed.BillingStatusRaw);
        var nextDueDateUtc = parsed.DueDateUtc ?? DateTime.UtcNow;
        Serilog.Log.Information("{EventType} | Assinatura webhook processado | SellerUserId={SellerUserId} | EventKey={EventKey} | Status={Status} | DueDate={DueDate} | IP={IpAddress}",
            "SUBSCRIPTION_WEBHOOK_PROCESSED", parsed.SellerUserId, parsed.EventKey, mappedBillingStatus, nextDueDateUtc, ipAddress);
        var gatewayChargeId = parsed.PaymentId ?? parsed.EventKey;

        var charge = await _dbContext.SellerSubscriptionChargeHistories
            .SingleOrDefaultAsync(x => x.GatewayChargeId == gatewayChargeId, cancellationToken);

        if (charge is null)
        {
            charge = new SellerSubscriptionChargeHistory
            {
                SellerUserId = parsed.SellerUserId.Value,
                GatewayChargeId = gatewayChargeId,
                ExternalReference = parsed.ExternalReference,
                GatewayStatus = parsed.BillingStatusRaw?.Trim().ToUpperInvariant() ?? "UNKNOWN",
                BillingStatus = mappedBillingStatus,
                DueDateUtc = nextDueDateUtc,
                PaidAtUtc = parsed.PaidAtUtc,
                Amount = parsed.Amount,
                RawPayload = rawPayload
            };

            await _dbContext.SellerSubscriptionChargeHistories.AddAsync(charge, cancellationToken);
        }
        else
        {
            charge.SellerUserId = parsed.SellerUserId.Value;
            charge.ExternalReference = parsed.ExternalReference;
            charge.GatewayStatus = parsed.BillingStatusRaw?.Trim().ToUpperInvariant() ?? charge.GatewayStatus;
            charge.BillingStatus = mappedBillingStatus;
            charge.DueDateUtc = nextDueDateUtc;
            charge.PaidAtUtc = parsed.PaidAtUtc;
            charge.Amount = parsed.Amount;
            charge.RawPayload = rawPayload;
            charge.MarkAsUpdated();
        }

        var current = await _dbContext.SellerSubscriptionStatuses
            .SingleOrDefaultAsync(x => x.SellerUserId == parsed.SellerUserId.Value, cancellationToken);

        if (current is null)
        {
            current = new SellerSubscriptionStatus
            {
                SellerUserId = parsed.SellerUserId.Value,
                NextDueDateUtc = nextDueDateUtc,
                BillingStatus = mappedBillingStatus
            };

            await _dbContext.SellerSubscriptionStatuses.AddAsync(current, cancellationToken);
        }
        else
        {
            current.BillingStatus = mappedBillingStatus;
            current.NextDueDateUtc = nextDueDateUtc;
            current.MarkAsUpdated();
        }

        var subscription = await _dbContext.SellerSubscriptions
            .SingleOrDefaultAsync(x => x.SellerUserId == parsed.SellerUserId.Value, cancellationToken);

        if (subscription is not null)
        {
            subscription.Status = mappedBillingStatus;
            subscription.NextBillingDateUtc = nextDueDateUtc;

            if (mappedBillingStatus == SellerSubscriptionBillingStatus.Blocked)
            {
                subscription.EndDateUtc = DateTime.UtcNow;
            }

            subscription.MarkAsUpdated();
        }

        await _dbContext.AuditLogs.AddAsync(new AuditLog
        {
            UserId = parsed.SellerUserId,
            Event = "AsaasWebhookProcessed",
            Entity = nameof(SellerSubscriptionStatus),
            EntityId = current.Id,
            Description = $"Asaas webhook processed with status {mappedBillingStatus} and due date {nextDueDateUtc:yyyy-MM-dd}.",
            IpAddress = ipAddress
        }, cancellationToken);

        await _subscriptionNotificationService.ProcessSellerSubscriptionNotificationsAsync(cancellationToken);

        return new ProcessWebhookResultDto
        {
            Processed = true
        };
    }

    private static SellerSubscriptionBillingStatus MapBillingStatus(string? billingStatusRaw)
    {
        return billingStatusRaw?.Trim().ToUpperInvariant() switch
        {
            "RECEIVED" => SellerSubscriptionBillingStatus.Active,
            "CONFIRMED" => SellerSubscriptionBillingStatus.Active,
            "OVERDUE" => SellerSubscriptionBillingStatus.Overdue,
            "PENDING" => SellerSubscriptionBillingStatus.Overdue,
            "REFUNDED" => SellerSubscriptionBillingStatus.Blocked,
            "RECEIVED_IN_CASH" => SellerSubscriptionBillingStatus.Active,
            _ => SellerSubscriptionBillingStatus.Overdue
        };
    }
}
