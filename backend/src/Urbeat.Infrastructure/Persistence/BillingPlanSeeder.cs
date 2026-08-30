using Urbeat.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.Infrastructure.Persistence;

/// <summary>
/// Garante a existência do plano padrão "Básico" (49,90) antes da criação de
/// lojas. Idempotente: preserva o registro se já existir e não duplica em
/// execuções repetidas. Não remove nem altera planos existentes.
/// </summary>
public sealed class BillingPlanSeeder
{
    public const string DefaultPlanName = "Básico";

    public const decimal DefaultPlanAmount = 49.90m;

    public const string DefaultPlanDescription = "Ideal para pequenos negócios. Taxa por pedido: 8%.";

    private readonly ApplicationDbContext _dbContext;

    public BillingPlanSeeder(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Plan> SeedAsync(CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.Plans
            .SingleOrDefaultAsync(x => x.Name == DefaultPlanName, cancellationToken);

        if (existing is not null)
        {
            if (!existing.IsActive)
            {
                existing.IsActive = true;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            return existing;
        }

        var plan = new Plan
        {
            Name = DefaultPlanName,
            Amount = DefaultPlanAmount,
            Description = DefaultPlanDescription,
            IsActive = true
        };

        await _dbContext.Plans.AddAsync(plan, cancellationToken);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return plan;
        }
        catch (DbUpdateException)
        {
            // Concorrência: outro processo/instância inseriu o plano primeiro.
            _dbContext.Entry(plan).State = EntityState.Detached;
            var winner = await _dbContext.Plans
                .SingleOrDefaultAsync(x => x.Name == DefaultPlanName, cancellationToken);
            return winner ?? plan;
        }
    }
}
