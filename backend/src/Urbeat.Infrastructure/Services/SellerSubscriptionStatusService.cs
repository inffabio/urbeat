using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

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

        var existing = await _dbContext.SellerSubscriptions
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.StoreId == request.StoreId, cancellationToken);

        if (existing is not null)
        {
            return new ContractSellerSubscriptionResultDto { AlreadyContracted = true };
        }

        var sellerUser = await _dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == sellerUserId, cancellationToken);

        if (sellerUser is null)
        {
            return new ContractSellerSubscriptionResultDto { Forbidden = true };
        }

        var asaasContract = await _asaasSubscriptionAdapter.CreateContractAsync(new AsaasSubscriptionContractRequest
        {
            SellerUserId = sellerUserId,
            SellerName = sellerUser.UserName ?? sellerUser.Email ?? "Seller",
            SellerEmail = sellerUser.Email ?? string.Empty,
            SellerPhone = store.PhoneNumber,
            PlanAmount = plan.Amount,
            FirstDueDateUtc = request.FirstDueDateUtc,
            ExternalReference = sellerUserId.ToString()
        }, cancellationToken);

        var now = DateTime.UtcNow;
        var subscription = new SellerSubscription
        {
            StoreId = store.Id,
            SellerUserId = sellerUserId,
            PlanId = plan.Id,
            PlanName = plan.Name,
            PlanAmount = plan.Amount,
            Status = SellerSubscriptionBillingStatus.Active,
            StartDateUtc = now,
            NextBillingDateUtc = asaasContract.NextDueDateUtc,
            GatewayCustomerId = asaasContract.GatewayCustomerId,
            GatewaySubscriptionId = asaasContract.GatewaySubscriptionId
        };

        await _dbContext.SellerSubscriptions.AddAsync(subscription, cancellationToken);

        var subscriptionStatus = await _dbContext.SellerSubscriptionStatuses
            .SingleOrDefaultAsync(x => x.SellerUserId == sellerUserId, cancellationToken);

        if (subscriptionStatus is null)
        {
            subscriptionStatus = new SellerSubscriptionStatus
            {
                SellerUserId = sellerUserId,
                NextDueDateUtc = asaasContract.NextDueDateUtc,
                BillingStatus = SellerSubscriptionBillingStatus.Active
            };

            await _dbContext.SellerSubscriptionStatuses.AddAsync(subscriptionStatus, cancellationToken);
        }
        else
        {
            subscriptionStatus.BillingStatus = SellerSubscriptionBillingStatus.Active;
            subscriptionStatus.NextDueDateUtc = asaasContract.NextDueDateUtc;
            subscriptionStatus.MarkAsUpdated();
        }

        store.IsSubscriptionBlocked = false;
        store.MarkAsUpdated();

        await _dbContext.AuditLogs.AddAsync(new AuditLog
        {
            UserId = sellerUserId,
            Event = "SellerSubscriptionContracted",
            Entity = nameof(SellerSubscription),
            EntityId = subscription.Id,
            Description = $"Seller contracted subscription {subscription.GatewaySubscriptionId}.",
            IpAddress = ipAddress
        }, cancellationToken);

        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        return new ContractSellerSubscriptionResultDto
        {
            Subscription = new ContractSellerSubscriptionResponseDto
            {
                SubscriptionId = subscription.Id,
                StoreId = subscription.StoreId,
                SellerUserId = subscription.SellerUserId,
                PlanName = subscription.PlanName,
                PlanAmount = subscription.PlanAmount,
                Status = subscription.Status,
                StartDateUtc = subscription.StartDateUtc,
                NextBillingDateUtc = subscription.NextBillingDateUtc,
                GatewayCustomerId = subscription.GatewayCustomerId,
                GatewaySubscriptionId = subscription.GatewaySubscriptionId
            }
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

        var storeBlocked = await _dbContext.Stores
            .AsNoTracking()
            .Where(x => x.OwnerUserId == sellerUserId)
            .Select(x => x.IsSubscriptionBlocked)
            .SingleOrDefaultAsync(cancellationToken);

        if (status is null && subscription is null)
        {
            return new SellerSubscriptionMyResponseDto
            {
                HasSubscription = false,
                LastChargeStatus = "Nao contratado",
                StoreBlocked = storeBlocked,
                RegularizationMessage = "Voce ainda nao possui assinatura ativa. Contrate um plano para operar a loja."
            };
        }

        var currentStatus = status?.BillingStatus ?? subscription!.Status;
        var nextDueDate = status?.NextDueDateUtc ?? subscription!.NextBillingDateUtc;

        return new SellerSubscriptionMyResponseDto
        {
            HasSubscription = true,
            PlanName = subscription?.PlanName ?? "Plano MVP",
            PlanAmount = subscription?.PlanAmount,
            BillingStatus = currentStatus,
            NextDueDateUtc = nextDueDate,
            LastChargeStatus = MapLastChargeStatus(currentStatus),
            StoreBlocked = storeBlocked,
            RegularizationMessage = BuildRegularizationMessage(currentStatus, nextDueDate)
        };
    }

    public async Task<IReadOnlyList<SellerSubscriptionChargeHistoryItemDto>> ListMyChargeHistoryAsync(
        Guid sellerUserId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.SellerSubscriptionChargeHistories
            .AsNoTracking()
            .Where(x => x.SellerUserId == sellerUserId)
            .OrderByDescending(x => x.DueDateUtc)
            .Select(x => new SellerSubscriptionChargeHistoryItemDto
            {
                GatewayChargeId = x.GatewayChargeId,
                GatewayStatus = x.GatewayStatus,
                BillingStatus = x.BillingStatus,
                DueDateUtc = x.DueDateUtc,
                PaidAtUtc = x.PaidAtUtc,
                Amount = x.Amount,
                ExternalReference = x.ExternalReference
            })
            .ToListAsync(cancellationToken);
    }

    private static string MapLastChargeStatus(SellerSubscriptionBillingStatus billingStatus)
    {
        return billingStatus switch
        {
            SellerSubscriptionBillingStatus.Active => "Pago",
            SellerSubscriptionBillingStatus.Overdue => "Vencido",
            SellerSubscriptionBillingStatus.Blocked => "Bloqueado",
            _ => "Desconhecido"
        };
    }

    private static string BuildRegularizationMessage(SellerSubscriptionBillingStatus billingStatus, DateTime nextDueDateUtc)
    {
        return billingStatus switch
        {
            SellerSubscriptionBillingStatus.Active => $"Assinatura ativa. Proximo vencimento em {nextDueDateUtc:yyyy-MM-dd}.",
            SellerSubscriptionBillingStatus.Overdue => $"Assinatura em atraso desde {nextDueDateUtc:yyyy-MM-dd}. Regularize para evitar bloqueio.",
            SellerSubscriptionBillingStatus.Blocked => $"Assinatura bloqueada. Regularize o pagamento vencido em {nextDueDateUtc:yyyy-MM-dd} para reativar a loja.",
            _ => "Verifique sua assinatura com o suporte."
        };
    }
}
