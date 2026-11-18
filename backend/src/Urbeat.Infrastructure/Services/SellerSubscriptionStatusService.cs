using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Domain.Services;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Urbeat.Infrastructure.Services;

public sealed class SellerSubscriptionStatusService : ISellerSubscriptionStatusService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IEfUnitOfWork _efUnitOfWork;
    private readonly IAsaasSubscriptionAdapter _asaasSubscriptionAdapter;

    public SellerSubscriptionStatusService(
        ApplicationDbContext dbContext,
        IEfUnitOfWork efUnitOfWork,
        IAsaasSubscriptionAdapter asaasSubscriptionAdapter)
    {
        _dbContext = dbContext;
        _efUnitOfWork = efUnitOfWork;
        _asaasSubscriptionAdapter = asaasSubscriptionAdapter;
    }

    public async Task<ContractSellerSubscriptionResultDto> ContractAsync(
        Guid sellerUserId,
        ContractSellerSubscriptionRequestDto request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var store = await _dbContext.Stores
            .SingleOrDefaultAsync(x => x.Id == request.StoreId, cancellationToken);

        if (store is null)
        {
            return new ContractSellerSubscriptionResultDto { NotFound = true };
        }

        if (store.OwnerUserId != sellerUserId)
        {
            return new ContractSellerSubscriptionResultDto { Forbidden = true };
        }

        var plan = await _dbContext.Plans
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.PlanId && x.IsActive, cancellationToken);

        if (plan is null)
        {
            return new ContractSellerSubscriptionResultDto { InvalidPlan = true };
        }

        var billingPeriod = request.EffectiveBillingPeriod;
        if (!plan.Supports(billingPeriod))
        {
            return new ContractSellerSubscriptionResultDto { InvalidBillingPeriod = true };
        }

        await using var transaction = _dbContext.Database.IsRelational()
            ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        if (transaction is not null)
        {
            // Serializa contratação/upgrade por loja. Duas requisições simultâneas
            // passam a observar o estado uma da outra em vez de inserirem em paralelo.
            await AcquireLockAsync($"seller-subscription:{store.Id}", cancellationToken);
        }

        var existing = await _dbContext.SellerSubscriptions
            .SingleOrDefaultAsync(x => x.StoreId == request.StoreId, cancellationToken);

        var activePlanCount = await _dbContext.Plans
            .AsNoTracking()
            .CountAsync(x => x.IsActive && (x.MonthlyEnabled || x.AnnualEnabled), cancellationToken);

        if (existing is not null && existing.PlanId == plan.Id)
        {
            return new ContractSellerSubscriptionResultDto { AlreadyContracted = true };
        }

        var currentLevel = existing?.PlanLevelSnapshot ?? 0;
        if (existing is not null && existing.PlanId is not null)
        {
            // Upgrade-only: o servidor nunca aceita a troca direta para um plano
            // de nível igual ou inferior, mesmo que o cliente chame a API.
            if (!plan.IsSuperiorTo(new Plan { Level = currentLevel }))
            {
                return new ContractSellerSubscriptionResultDto { DowngradeNotAllowed = true };
            }

            if (activePlanCount <= 1)
            {
                return new ContractSellerSubscriptionResultDto { AlreadyContracted = true };
            }
        }

        var sellerUser = await _dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == sellerUserId, cancellationToken);

        if (sellerUser is null)
        {
            return new ContractSellerSubscriptionResultDto { Forbidden = true };
        }

        var now = DateTime.UtcNow;
        var subscription = existing ?? new SellerSubscription
        {
            StoreId = store.Id,
            SellerUserId = sellerUserId
        };

        if (existing is not null)
        {
            await _dbContext.SellerSubscriptionChanges.AddAsync(new SellerSubscriptionChange
            {
                SubscriptionId = existing.Id,
                SellerUserId = sellerUserId,
                PreviousPlanId = existing.PlanId,
                PreviousPlanName = existing.PlanName,
                PreviousPlanAmount = existing.PlanAmount,
                PreviousBillingPeriod = existing.BillingPeriod,
                PreviousPlanLevel = existing.PlanLevelSnapshot,
                NewPlanId = plan.Id,
                NewPlanName = plan.Name,
                NewPlanAmount = plan.GetAmountFor(billingPeriod),
                NewBillingPeriod = billingPeriod,
                NewPlanLevel = plan.Level,
                ChangedAtUtc = now,
                ChangedByUserId = sellerUserId,
                Reason = "Upgrade"
            }, cancellationToken);
        }

        // O upgrade aplica as condições do novo plano a partir da mudança e não
        // concede um novo trial: o primeiro vencimento é imediato.
        var trialStartUtc = now;
        var trialDays = existing is null ? plan.TrialDays : 0;
        var dueDateUtc = SubscriptionBillingCalculator.CalculateFirstChargeDueUtc(trialDays, trialStartUtc, now);

        ApplyPlanSnapshot(subscription, plan, billingPeriod, trialStartUtc, now, dueDateUtc, grantTrial: existing is null);

        if (existing is null)
        {
            await _dbContext.SellerSubscriptions.AddAsync(subscription, cancellationToken);
        }
        else
        {
            subscription.MarkAsUpdated();
        }

        await AddPendingChargeAsync(
            subscription,
            plan,
            billingPeriod,
            dueDateUtc,
            $"subscription:{subscription.Id}:{plan.Id}:{billingPeriod}:{existing?.PlanId?.ToString() ?? "initial"}",
            sellerUserId,
            store.Id,
            cancellationToken);

        var subscriptionStatus = await _dbContext.SellerSubscriptionStatuses
            .SingleOrDefaultAsync(x => x.SellerUserId == sellerUserId, cancellationToken);

        if (subscriptionStatus is null)
        {
            subscriptionStatus = new SellerSubscriptionStatus
            {
                SellerUserId = sellerUserId,
                NextDueDateUtc = dueDateUtc,
                BillingStatus = SellerSubscriptionBillingStatus.Active
            };

            await _dbContext.SellerSubscriptionStatuses.AddAsync(subscriptionStatus, cancellationToken);
        }
        else
        {
            // Um upgrade não regulariza inadimplência: o status vigente é preservado.
            if (existing is null)
            {
                subscriptionStatus.BillingStatus = SellerSubscriptionBillingStatus.Active;
            }

            subscriptionStatus.NextDueDateUtc = dueDateUtc;
            subscriptionStatus.MarkAsUpdated();
        }

        await _dbContext.AuditLogs.AddAsync(new AuditLog
        {
            UserId = sellerUserId,
            Event = existing is null ? "SellerSubscriptionCreated" : "SellerSubscriptionUpgraded",
            Entity = nameof(SellerSubscription),
            EntityId = subscription.Id,
            Description = existing is null
                ? $"Seller subscription created for plan '{plan.Name}' ({billingPeriod}) awaiting gateway integration."
                : $"Seller subscription upgraded to plan '{plan.Name}' ({billingPeriod}) awaiting gateway integration.",
            IpAddress = ipAddress
        }, cancellationToken);

        try
        {
            await _efUnitOfWork.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            _dbContext.ChangeTracker.Clear();
            return new ContractSellerSubscriptionResultDto { AlreadyContracted = true };
        }

        return new ContractSellerSubscriptionResultDto
        {
            Subscription = MapContractResponse(subscription)
        };
    }

    public async Task UpsertAsync(UpsertSellerSubscriptionStatusRequestDto request, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.SellerSubscriptionStatuses
            .SingleOrDefaultAsync(x => x.SellerUserId == request.SellerUserId, cancellationToken);

        if (entity is null)
        {
            entity = new SellerSubscriptionStatus
            {
                SellerUserId = request.SellerUserId,
                NextDueDateUtc = request.NextDueDateUtc,
                BillingStatus = request.BillingStatus
            };

            await _dbContext.SellerSubscriptionStatuses.AddAsync(entity, cancellationToken);
        }
        else
        {
            entity.NextDueDateUtc = request.NextDueDateUtc;
            entity.BillingStatus = request.BillingStatus;
            entity.MarkAsUpdated();
        }

        await _efUnitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<SellerSubscriptionMyResponseDto> GetMySubscriptionAsync(Guid sellerUserId, CancellationToken cancellationToken = default)
    {
        var subscription = await _dbContext.SellerSubscriptions
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.SellerUserId == sellerUserId, cancellationToken);

        var status = await _dbContext.SellerSubscriptionStatuses
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.SellerUserId == sellerUserId, cancellationToken);

        var store = await _dbContext.Stores
            .AsNoTracking()
            .Where(x => x.OwnerUserId == sellerUserId)
            .Select(x => new { x.Id, x.IsSubscriptionBlocked })
            .SingleOrDefaultAsync(cancellationToken);

        var pendingCharge = await _dbContext.SellerSubscriptionCharges
            .AsNoTracking()
            .Where(x => x.SellerUserId == sellerUserId
                        && x.Status == SellerSubscriptionChargeStatus.AwaitingGateway)
            .OrderByDescending(x => x.DueDateUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var storeBlocked = store?.IsSubscriptionBlocked ?? false;
        var storeId = subscription?.StoreId ?? store?.Id;

        if (status is null && subscription is null)
        {
            return new SellerSubscriptionMyResponseDto
            {
                HasSubscription = false,
                StoreId = storeId,
                LastChargeStatus = "Nao contratado",
                StoreBlocked = storeBlocked,
                RegularizationMessage = "Voce ainda nao possui assinatura ativa. Contrate um plano para operar a loja."
            };
        }

        var plan = subscription?.PlanId is Guid planId
            ? await _dbContext.Plans
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == planId, cancellationToken)
            : null;

        var currentStatus = status?.BillingStatus ?? subscription!.Status;
        var nextDueDate = status?.NextDueDateUtc ?? subscription!.NextBillingDateUtc;
        var hasGatewaySubscription = subscription is not null
            && !string.IsNullOrWhiteSpace(subscription.GatewaySubscriptionId);

        var isLegacySnapshot = subscription is not null
            && string.IsNullOrEmpty(subscription.PlanDescriptionSnapshot)
            && subscription.PlanLevelSnapshot == 0
            && subscription.TrialDaysSnapshot == 0
            && subscription.TrialEndsAtUtc is null;

        var trialDays = subscription is not null && !isLegacySnapshot
            ? subscription.TrialDaysSnapshot
            : plan?.TrialDays;

        var trialEndsAtUtc = subscription?.TrialEndsAtUtc;
        if (trialEndsAtUtc is null && subscription is not null && isLegacySnapshot && plan is { TrialDays: > 0 })
        {
            // Assinaturas legadas anteriores ao snapshot de trial continuam
            // exibindo o trial calculado a partir do início persistido.
            trialEndsAtUtc = subscription.StartDateUtc.AddDays(plan.TrialDays);
        }
        else if (trialEndsAtUtc is null && subscription is not null && plan is null && !hasGatewaySubscription)
        {
            trialEndsAtUtc = subscription.NextBillingDateUtc;
        }

        var isTrialActive = !hasGatewaySubscription
            && trialEndsAtUtc.HasValue
            && trialEndsAtUtc.Value > DateTime.UtcNow
            && currentStatus is not (SellerSubscriptionBillingStatus.Overdue or SellerSubscriptionBillingStatus.Blocked);

        var nextChargeAmount = subscription?.PlanAmount ?? plan?.Amount;

        return new SellerSubscriptionMyResponseDto
        {
            HasSubscription = true,
            StoreId = storeId,
            PlanId = subscription?.PlanId,
            PlanName = subscription?.PlanName ?? plan?.Name ?? "Plano MVP",
            PlanDescription = subscription?.PlanDescriptionSnapshot ?? plan?.Description,
            PlanAmount = subscription?.PlanAmount,
            BillingPeriod = subscription?.BillingPeriod ?? SubscriptionBillingPeriod.Monthly,
            PlanLevel = subscription?.PlanLevelSnapshot,
            NextChargeAmount = nextChargeAmount,
            TrialDays = trialDays,
            TrialEndsAtUtc = trialEndsAtUtc,
            IsTrialActive = isTrialActive,
            HasGatewaySubscription = hasGatewaySubscription,
            HasPendingGatewayCharge = pendingCharge is not null,
            PendingChargeAmount = pendingCharge?.Amount,
            PendingChargeDueDateUtc = pendingCharge?.DueDateUtc,
            BillingStatus = currentStatus,
            NextDueDateUtc = nextDueDate,
            LastChargeStatus = MapLastChargeStatus(
                currentStatus,
                isTrialActive,
                hasGatewaySubscription,
                pendingCharge is not null),
            StoreBlocked = storeBlocked,
            RegularizationMessage = BuildRegularizationMessage(
                currentStatus,
                nextDueDate,
                isTrialActive,
                trialEndsAtUtc,
                pendingCharge is not null)
        };
    }

    public async Task<IReadOnlyList<SellerSubscriptionChargeHistoryItemDto>> ListMyChargeHistoryAsync(
        Guid sellerUserId,
        CancellationToken cancellationToken = default)
    {
        var gatewayCharges = await _dbContext.SellerSubscriptionChargeHistories
            .AsNoTracking()
            .Where(x => x.SellerUserId == sellerUserId)
            .Select(x => new SellerSubscriptionChargeHistoryItemDto
            {
                GatewayChargeId = x.GatewayChargeId,
                GatewayStatus = x.GatewayStatus,
                BillingStatus = x.BillingStatus,
                DueDateUtc = x.DueDateUtc,
                BillingPeriodStartUtc = x.BillingPeriodStartUtc,
                BillingPeriodEndUtc = x.BillingPeriodEndUtc,
                PaidAtUtc = x.PaidAtUtc,
                Amount = x.Amount,
                ExternalReference = x.ExternalReference,
                CreatedAtUtc = x.CreatedAtUtc,
                IsGatewayPending = false
            })
            .ToListAsync(cancellationToken);

        var pendingCharges = await _dbContext.SellerSubscriptionCharges
            .AsNoTracking()
            .Where(x => x.SellerUserId == sellerUserId)
            .Select(x => new SellerSubscriptionChargeHistoryItemDto
            {
                GatewayChargeId = "stub:" + x.Id,
                GatewayStatus = "awaiting_gateway",
                BillingStatus = x.DueDateUtc < DateTime.UtcNow
                    ? SellerSubscriptionBillingStatus.Overdue
                    : SellerSubscriptionBillingStatus.Active,
                DueDateUtc = x.DueDateUtc,
                BillingPeriodStartUtc = x.BillingPeriodStartUtc,
                BillingPeriodEndUtc = x.BillingPeriodEndUtc,
                Amount = x.Amount,
                CreatedAtUtc = x.CreatedAtUtc,
                IsGatewayPending = true
            })
            .ToListAsync(cancellationToken);

        return gatewayCharges
            .Concat(pendingCharges)
            .OrderByDescending(x => x.DueDateUtc)
            .ThenByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.GatewayChargeId, StringComparer.Ordinal)
            .ToList();
    }

    private async Task AddPendingChargeAsync(
        SellerSubscription subscription,
        Plan plan,
        SubscriptionBillingPeriod billingPeriod,
        DateTime dueDateUtc,
        string idempotencyKey,
        Guid sellerUserId,
        Guid storeId,
        CancellationToken cancellationToken)
    {
        var alreadyExists = await _dbContext.SellerSubscriptionCharges
            .AsNoTracking()
            .AnyAsync(x => x.IdempotencyKey == idempotencyKey, cancellationToken);

        if (alreadyExists)
        {
            return;
        }

        await _dbContext.SellerSubscriptionCharges.AddAsync(new SellerSubscriptionCharge
        {
            SellerUserId = sellerUserId,
            StoreId = storeId,
            SubscriptionId = subscription.Id,
            PlanId = plan.Id,
            PlanNameSnapshot = plan.Name,
            Amount = plan.GetAmountFor(billingPeriod),
            BillingPeriod = billingPeriod,
            Status = SellerSubscriptionChargeStatus.AwaitingGateway,
            DueDateUtc = dueDateUtc,
            BillingPeriodStartUtc = dueDateUtc,
            BillingPeriodEndUtc = SubscriptionBillingCalculator.CalculatePeriodEndUtc(dueDateUtc, billingPeriod),
            IdempotencyKey = idempotencyKey
        }, cancellationToken);
    }

    private async Task AcquireLockAsync(string key, CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational())
        {
            return;
        }

        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({key}))",
            cancellationToken);
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
        => exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        };

    private static void ApplyPlanSnapshot(
        SellerSubscription subscription,
        Plan plan,
        SubscriptionBillingPeriod billingPeriod,
        DateTime startUtc,
        DateTime nowUtc,
        DateTime nextBillingDateUtc,
        bool grantTrial)
    {
        subscription.PlanId = plan.Id;
        subscription.PlanName = plan.Name;
        subscription.PlanDescriptionSnapshot = plan.Description;
        subscription.PlanAmount = plan.GetAmountFor(billingPeriod);
        subscription.BillingPeriod = billingPeriod;
        subscription.TrialDaysSnapshot = plan.TrialDays;
        subscription.AnnualDiscountPercentSnapshot = plan.AnnualDiscountPercent;
        subscription.PlanLevelSnapshot = plan.Level;
        subscription.Status = SellerSubscriptionBillingStatus.Active;
        subscription.StartDateUtc = startUtc;
        subscription.TrialEndsAtUtc = grantTrial && plan.TrialDays > 0
            ? startUtc.AddDays(plan.TrialDays)
            : null;
        subscription.NextBillingDateUtc = nextBillingDateUtc;
        subscription.EndDateUtc = null;
    }

    private static ContractSellerSubscriptionResponseDto MapContractResponse(SellerSubscription subscription)
        => new()
        {
            SubscriptionId = subscription.Id,
            StoreId = subscription.StoreId,
            SellerUserId = subscription.SellerUserId,
            PlanName = subscription.PlanName,
            PlanDescription = subscription.PlanDescriptionSnapshot,
            PlanAmount = subscription.PlanAmount,
            BillingPeriod = subscription.BillingPeriod,
            Status = subscription.Status,
            StartDateUtc = subscription.StartDateUtc,
            TrialEndsAtUtc = subscription.TrialEndsAtUtc,
            NextBillingDateUtc = subscription.NextBillingDateUtc,
            GatewayCustomerId = subscription.GatewayCustomerId,
            GatewaySubscriptionId = subscription.GatewaySubscriptionId,
            AwaitingGateway = true
        };

    private static string MapLastChargeStatus(
        SellerSubscriptionBillingStatus billingStatus,
        bool isTrialActive,
        bool hasGatewaySubscription,
        bool hasPendingGatewayCharge)
    {
        // Nunca reportar pagamento quando a loja esta apenas no periodo gratuito,
        // possui somente a assinatura local ou aguarda integração de gateway.
        if (isTrialActive)
        {
            return "Periodo gratuito";
        }

        if (hasPendingGatewayCharge)
        {
            return "Boleto aguardando integração";
        }

        if (!hasGatewaySubscription && billingStatus == SellerSubscriptionBillingStatus.Active)
        {
            return "Nao contratado";
        }

        return billingStatus switch
        {
            SellerSubscriptionBillingStatus.Active => "Pago",
            SellerSubscriptionBillingStatus.Overdue => "Vencido",
            SellerSubscriptionBillingStatus.Blocked => "Bloqueado",
            _ => "Desconhecido"
        };
    }

    /// <summary>
    /// Calcula a primeira cobrança sem confiar no cliente. O trial começa no
    /// início da assinatura local e não é estendido por contratações tardias:
    /// se o período já venceu, a primeira cobrança vence imediatamente.
    /// </summary>
    internal static DateTime CalculateFirstDueDateUtc(int trialDays, DateTime trialStartUtc, DateTime nowUtc)
        => SubscriptionBillingCalculator.CalculateFirstChargeDueUtc(trialDays, trialStartUtc, nowUtc);

    private static string BuildRegularizationMessage(
        SellerSubscriptionBillingStatus billingStatus,
        DateTime nextDueDateUtc,
        bool isTrialActive,
        DateTime? trialEndsAtUtc,
        bool hasPendingGatewayCharge)
    {
        if (isTrialActive)
        {
            return $"Periodo gratuito ativo ate {trialEndsAtUtc:yyyy-MM-dd}. A primeira cobranca vence em {nextDueDateUtc:yyyy-MM-dd}.";
        }

        if (hasPendingGatewayCharge && billingStatus == SellerSubscriptionBillingStatus.Active)
        {
            return "Assinatura ativa. O boleto da primeira cobrança está aguardando a integração de gateway.";
        }

        return billingStatus switch
        {
            SellerSubscriptionBillingStatus.Active => $"Assinatura ativa. Proximo vencimento em {nextDueDateUtc:yyyy-MM-dd}.",
            SellerSubscriptionBillingStatus.Overdue => $"Assinatura em atraso desde {nextDueDateUtc:yyyy-MM-dd}. Regularize para evitar bloqueio.",
            SellerSubscriptionBillingStatus.Blocked => $"Assinatura bloqueada. Regularize o pagamento vencido em {nextDueDateUtc:yyyy-MM-dd} para reativar a loja.",
            _ => "Verifique sua assinatura com o suporte."
        };
    }
}
