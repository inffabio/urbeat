using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Plans;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Urbeat.Infrastructure.Services;

public sealed class PlanService : IPlanService
{
    public const string DefaultGatewayProvider = "Asaas";

    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    private readonly ApplicationDbContext _dbContext;
    private readonly IEfUnitOfWork _efUnitOfWork;

    public PlanService(
        ApplicationDbContext dbContext,
        IEfUnitOfWork efUnitOfWork)
    {
        _dbContext = dbContext;
        _efUnitOfWork = efUnitOfWork;
    }

    public async Task<PagedPlanResponseDto> ListPagedAsync(
        PlanPageQueryDto query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = query.Page < 1 ? 1 : Math.Min(query.Page, 1_000_000);
        var pageSize = query.PageSize < 1 ? DefaultPageSize : Math.Min(query.PageSize, MaxPageSize);

        // Count on the unordered set, then order and page: the catalog keeps a deterministic
        // level/name order so page boundaries never repeat or skip a plan.
        var baseQuery = _dbContext.Plans.AsNoTracking();
        var totalItems = await baseQuery.CountAsync(cancellationToken);
        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);

        var plans = await baseQuery
            .OrderBy(x => x.Level)
            .ThenBy(x => x.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedPlanResponseDto
        {
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = totalPages,
            Items = plans.Select(Map).ToList()
        };
    }

    public async Task<IReadOnlyList<PlanResponseDto>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        var plans = await _dbContext.Plans
            .AsNoTracking()
            .OrderBy(x => x.Level)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return plans.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<PlanResponseDto>> ListActiveAsync(CancellationToken cancellationToken = default)
    {
        var plans = await _dbContext.Plans
            .AsNoTracking()
            .Where(x => x.IsActive && (x.MonthlyEnabled || x.AnnualEnabled))
            .OrderBy(x => x.Level)
            .ThenBy(x => x.Amount)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return plans.Select(Map).ToList();
    }

    public async Task<PublicPlanOfferResponseDto> GetPublicOfferAsync(CancellationToken cancellationToken = default)
    {
        var plans = await _dbContext.Plans
            .AsNoTracking()
            .Where(x => x.IsActive && (x.MonthlyEnabled || x.AnnualEnabled))
            .OrderBy(x => x.Level)
            .ThenBy(x => x.Amount)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return new PublicPlanOfferResponseDto
        {
            Plans = plans.Select(MapPublic).ToList()
        };
    }

    public async Task<PlanResponseDto?> CreateAsync(CreatePlanRequestDto request, CancellationToken cancellationToken = default)
    {
        var normalizedName = request.Name.Trim();
        var exists = await _dbContext.Plans
            .AsNoTracking()
            .AnyAsync(x => x.Name == normalizedName, cancellationToken);

        if (exists)
        {
            return null;
        }

        var plan = new Plan
        {
            Name = normalizedName,
            Amount = request.EffectiveMonthlyAmount,
            Description = request.Description?.Trim() ?? string.Empty,
            IsActive = request.IsActive,
            TrialDays = request.TrialDays ?? Plan.DefaultTrialDays,
            MonthlyEnabled = request.EffectiveMonthlyEnabled,
            AnnualEnabled = request.EffectiveAnnualEnabled,
            AnnualDiscountPercent = request.EffectiveAnnualDiscountPercent,
            Level = request.EffectiveLevel,
            BenefitsJson = PlanBenefits.Serialize(request.Benefits)
        };

        await _dbContext.Plans.AddAsync(plan, cancellationToken);
        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        return Map(plan);
    }

    public async Task<PlanResponseDto?> UpdateAsync(Guid planId, UpdatePlanRequestDto request, CancellationToken cancellationToken = default)
    {
        var plan = await _dbContext.Plans
            .SingleOrDefaultAsync(x => x.Id == planId, cancellationToken);

        if (plan is null)
        {
            return null;
        }

        var normalizedName = request.Name.Trim();
        var duplicateName = await _dbContext.Plans
            .AsNoTracking()
            .AnyAsync(x => x.Id != planId && x.Name == normalizedName, cancellationToken);

        if (duplicateName)
        {
            return null;
        }

        plan.Name = normalizedName;
        plan.Amount = request.EffectiveMonthlyAmount ?? plan.Amount;
        plan.Description = request.Description?.Trim() ?? string.Empty;
        plan.TrialDays = request.TrialDays ?? plan.TrialDays;
        plan.MonthlyEnabled = request.MonthlyEnabled ?? plan.MonthlyEnabled;
        plan.AnnualEnabled = request.AnnualEnabled ?? plan.AnnualEnabled;
        plan.AnnualDiscountPercent = request.AnnualDiscountPercent ?? plan.AnnualDiscountPercent;
        plan.Level = request.Level ?? plan.Level;

        if (request.Benefits is not null)
        {
            plan.BenefitsJson = PlanBenefits.Serialize(request.Benefits);
        }

        // Uma edição não pode deixar o plano sem nenhuma periodicidade contratável.
        if (!plan.MonthlyEnabled && !plan.AnnualEnabled)
        {
            plan.MonthlyEnabled = true;
        }

        plan.MarkAsUpdated();

        await _efUnitOfWork.SaveChangesAsync(cancellationToken);
        return Map(plan);
    }

    public async Task<PlanResponseDto?> UpdateStatusAsync(Guid planId, bool isActive, CancellationToken cancellationToken = default)
    {
        var plan = await _dbContext.Plans
            .SingleOrDefaultAsync(x => x.Id == planId, cancellationToken);

        if (plan is null)
        {
            return null;
        }

        // Desativar o plano não altera assinaturas existentes: os snapshots já
        // gravados continuam valendo e não são recalculados aqui.
        plan.IsActive = isActive;
        plan.MarkAsUpdated();
        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        return Map(plan);
    }

    public async Task<GatewayConfigurationResponseDto?> GetGatewayConfigurationAsync(
        Guid planId,
        CancellationToken cancellationToken = default)
    {
        var planExists = await _dbContext.Plans
            .AsNoTracking()
            .AnyAsync(x => x.Id == planId, cancellationToken);

        if (!planExists)
        {
            return null;
        }

        var configuration = await _dbContext.SubscriptionGatewayConfigurations
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.PlanId == planId, cancellationToken);

        return configuration is null
            ? EmptyGatewayConfiguration(planId)
            : MapGateway(configuration);
    }

    public async Task<GatewayConfigurationResponseDto?> UpdateGatewayConfigurationAsync(
        Guid planId,
        UpdateGatewayConfigurationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var planExists = await _dbContext.Plans
            .AsNoTracking()
            .AnyAsync(x => x.Id == planId, cancellationToken);

        if (!planExists)
        {
            return null;
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using var transaction = _dbContext.Database.IsRelational()
                ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
                : null;

            if (transaction is not null)
            {
                // Serializa o upsert por plano. O índice único continua sendo a
                // autoridade para writers externos; o catch abaixo cobre a corrida.
                await AcquireLockAsync($"plan-gateway:{planId}", cancellationToken);
            }

            try
            {
                var configuration = await _dbContext.SubscriptionGatewayConfigurations
                    .SingleOrDefaultAsync(x => x.PlanId == planId, cancellationToken);

                if (configuration is null)
                {
                    configuration = new SubscriptionGatewayConfiguration { PlanId = planId };
                    await _dbContext.SubscriptionGatewayConfigurations.AddAsync(configuration, cancellationToken);
                }

                ApplyGatewayConfiguration(configuration, request);
                configuration.MarkAsUpdated();

                await _efUnitOfWork.SaveChangesAsync(cancellationToken);

                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }

                return MapGateway(configuration);
            }
            catch (DbUpdateException exception) when (IsUniqueViolation(exception))
            {
                if (transaction is not null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                }

                _dbContext.ChangeTracker.Clear();

                if (attempt == 1)
                {
                    // Writer externo persistente: devolve o estado atual em vez de 500.
                    return await GetGatewayConfigurationAsync(planId, cancellationToken);
                }
            }
        }

        // Duas violações consecutivas indicam writer externo persistente. Devolve
        // o estado atual de forma determinística em vez de 500.
        return await GetGatewayConfigurationAsync(planId, cancellationToken);
    }

    private static void ApplyGatewayConfiguration(
        SubscriptionGatewayConfiguration configuration,
        UpdateGatewayConfigurationRequestDto request)
    {
        configuration.Provider = string.IsNullOrWhiteSpace(request.Provider)
            ? DefaultGatewayProvider
            : request.Provider.Trim();
        configuration.Environment = string.IsNullOrWhiteSpace(request.Environment)
            ? "Sandbox"
            : request.Environment.Trim();
        configuration.ApiBaseUrl = request.ApiBaseUrl?.Trim() ?? string.Empty;
        configuration.WebhookUrl = request.WebhookUrl?.Trim() ?? string.Empty;
        configuration.IsEnabled = request.IsEnabled;

        // Referências apontam para o Vault/secret store; nenhum segredo em texto
        // plano é gravado neste banco. Null preserva; string vazia remove.
        if (request.ApiKeySecretReference is not null)
        {
            configuration.ApiKeySecretReference = request.ApiKeySecretReference.Trim();
        }

        if (request.WebhookSecretReference is not null)
        {
            configuration.WebhookSecretReference = request.WebhookSecretReference.Trim();
        }
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

    private static PlanResponseDto Map(Plan plan)
    {
        return new PlanResponseDto
        {
            Id = plan.Id,
            Name = plan.Name,
            Amount = plan.MonthlyAmount,
            MonthlyAmount = plan.MonthlyAmount,
            AnnualAmount = plan.AnnualAmount,
            AnnualDiscountPercent = plan.AnnualDiscountPercent,
            MonthlyEnabled = plan.MonthlyEnabled,
            AnnualEnabled = plan.AnnualEnabled,
            Description = plan.Description,
            IsActive = plan.IsActive,
            TrialDays = plan.TrialDays,
            Benefits = PlanBenefits.Deserialize(plan.BenefitsJson),
            Level = plan.Level
        };
    }

    private static PublicPlanOfferItemDto MapPublic(Plan plan)
    {
        return new PublicPlanOfferItemDto
        {
            Id = plan.Id,
            Name = plan.Name,
            Amount = plan.MonthlyAmount,
            MonthlyAmount = plan.MonthlyAmount,
            AnnualAmount = plan.AnnualAmount,
            AnnualDiscountPercent = plan.AnnualDiscountPercent,
            MonthlyEnabled = plan.MonthlyEnabled,
            AnnualEnabled = plan.AnnualEnabled,
            Description = plan.Description,
            TrialDays = plan.TrialDays,
            Benefits = PlanBenefits.Deserialize(plan.BenefitsJson),
            Level = plan.Level
        };
    }

    private static GatewayConfigurationResponseDto EmptyGatewayConfiguration(Guid planId) => new()
    {
        Id = Guid.Empty,
        PlanId = planId,
        Provider = DefaultGatewayProvider,
        Environment = "Sandbox",
        ApiBaseUrl = string.Empty,
        WebhookUrl = string.Empty,
        HasApiKey = false,
        HasWebhookSecret = false,
        IsEnabled = false,
        UpdatedAtUtc = null
    };

    private static GatewayConfigurationResponseDto MapGateway(SubscriptionGatewayConfiguration configuration) => new()
    {
        Id = configuration.Id,
        PlanId = configuration.PlanId,
        Provider = configuration.Provider,
        Environment = configuration.Environment,
        ApiBaseUrl = configuration.ApiBaseUrl,
        WebhookUrl = configuration.WebhookUrl,
        HasApiKey = !string.IsNullOrWhiteSpace(configuration.ApiKeySecretReference),
        HasWebhookSecret = !string.IsNullOrWhiteSpace(configuration.WebhookSecretReference),
        IsEnabled = configuration.IsEnabled,
        UpdatedAtUtc = configuration.UpdatedAtUtc ?? configuration.CreatedAtUtc
    };
}
