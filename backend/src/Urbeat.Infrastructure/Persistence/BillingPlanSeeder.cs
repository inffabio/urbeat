using System.Text.Json;
using Urbeat.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.Infrastructure.Persistence;

/// <summary>
/// Garante o catálogo inicial de planos por porte. Os valores são apenas seed
/// administrativo: depois de criados, nome, preço, trial e benefícios passam a
/// ser configurados pelo admin e não são sobrescritos nas próximas execuções.
/// Planos legados (por exemplo "Básico") não são removidos nem alterados.
/// </summary>
public sealed class BillingPlanSeeder
{
    public const string DefaultPlanName = "Começar";

    public const decimal DefaultPlanAmount = 19.90m;

    public const string DefaultPlanDescription = "Para quem está começando a vender pelo próprio app.";

    public const int DefaultTrialDays = Plan.DefaultTrialDays;

    public sealed record PlanSeed(
        string Name,
        decimal Amount,
        string Description,
        IReadOnlyList<string> Benefits);

    public static readonly IReadOnlyList<PlanSeed> Catalog =
    [
        new(
            DefaultPlanName,
            DefaultPlanAmount,
            DefaultPlanDescription,
            [
                "Cardápio digital com a sua marca",
                "Pedidos no WhatsApp",
                "Painel de pedidos em tempo real",
                "Impressão automática na cozinha",
                "Suporte via WhatsApp"
            ]),
        new(
            "Crescer",
            39.90m,
            "Para negócios que já vendem e querem mais controle.",
            [
                "Tudo do plano Começar",
                "Relatórios de vendas",
                "Base de clientes exportável",
                "Banners no cardápio",
                "Geração de QR Code"
            ]),
        new(
            "Profissional",
            79.90m,
            "Para operações com equipe e automações avançadas.",
            [
                "Tudo do plano Crescer",
                "Usuários e permissões",
                "Automação de WhatsApp",
                "Dashboard com resumo das vendas",
                "Controle de finanças"
            ])
    ];

    private readonly ApplicationDbContext _dbContext;

    public BillingPlanSeeder(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Plan> SeedAsync(CancellationToken cancellationToken = default)
    {
        var existingNames = await _dbContext.Plans
            .AsNoTracking()
            .Select(x => x.Name)
            .ToListAsync(cancellationToken);

        var created = new List<Plan>();

        foreach (var seed in Catalog)
        {
            if (existingNames.Contains(seed.Name, StringComparer.Ordinal))
            {
                continue;
            }

            var plan = new Plan
            {
                Name = seed.Name,
                Amount = seed.Amount,
                Description = seed.Description,
                IsActive = true,
                TrialDays = DefaultTrialDays,
                BenefitsJson = JsonSerializer.Serialize(seed.Benefits)
            };

            await _dbContext.Plans.AddAsync(plan, cancellationToken);
            created.Add(plan);
        }

        if (created.Count > 0)
        {
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Concorrência: outra instância pode ter inserido o catálogo primeiro.
                foreach (var plan in created)
                {
                    _dbContext.Entry(plan).State = EntityState.Detached;
                }
            }
        }

        var defaultPlan = await _dbContext.Plans
            .SingleOrDefaultAsync(x => x.Name == DefaultPlanName, cancellationToken);

        return defaultPlan ?? created.FirstOrDefault(x => x.Name == DefaultPlanName)
            ?? throw new InvalidOperationException($"Default plan '{DefaultPlanName}' could not be seeded.");
    }
}
