using System.ComponentModel.DataAnnotations.Schema;

namespace Urbeat.Domain.Entities;

public sealed class Plan : BaseEntity
{
    /// <summary>
    /// Dias de teste gratuito aplicados a novas lojas/assinaturas deste plano.
    /// O trial é por plano (não global); zero significa cobrança já no próximo ciclo.
    /// </summary>
    public const int DefaultTrialDays = 15;

    public const int MaxTrialDays = 365;

    public const int MaxAnnualDiscountPercent = 100;

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Preço mensal base do plano. A coluna histórica chama-se <c>Amount</c>;
    /// <see cref="MonthlyAmount"/> é o nome de domínio usado pelos novos
    /// contratos e nunca sobrescreve valores vindos do cliente no servidor.
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>Preço mensal autoritativo (alias de <see cref="Amount"/>).</summary>
    [NotMapped]
    public decimal MonthlyAmount
    {
        get => Amount;
        set => Amount = value;
    }

    public string Description { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public int TrialDays { get; set; } = DefaultTrialDays;

    /// <summary>Habilita a contratação com cobrança mensal.</summary>
    public bool MonthlyEnabled { get; set; } = true;

    /// <summary>Habilita a contratação com cobrança anual.</summary>
    public bool AnnualEnabled { get; set; }

    /// <summary>Desconto percentual aplicado ao total de 12 meses na cobrança anual.</summary>
    public int AnnualDiscountPercent { get; set; }

    /// <summary>
    /// Nível administrativo do plano. Planos superiores têm nível maior; o
    /// nível define upgrade e nunca é inferido exclusivamente do preço.
    /// </summary>
    public int Level { get; set; }

    /// <summary>
    /// Benefícios informativos do plano serializados como JSON (array de strings).
    /// Não representa autorização de recursos; a autorização futura deve ser feita no servidor.
    /// </summary>
    public string BenefitsJson { get; set; } = "[]";

    /// <summary>Total anual calculado a partir do preço mensal e do desconto.</summary>
    public decimal AnnualAmount => CalculateAnnualAmount(Amount, AnnualDiscountPercent);

    public bool Supports(SubscriptionBillingPeriod period) => period switch
    {
        SubscriptionBillingPeriod.Monthly => MonthlyEnabled,
        SubscriptionBillingPeriod.Annual => AnnualEnabled,
        _ => false
    };

    public decimal GetAmountFor(SubscriptionBillingPeriod period)
    {
        if (!Supports(period))
        {
            throw new InvalidOperationException($"O plano não oferece a periodicidade {period}.");
        }

        return period == SubscriptionBillingPeriod.Annual ? AnnualAmount : Amount;
    }

    public bool IsSuperiorTo(Plan other) => Level > other.Level;

    public static decimal CalculateAnnualAmount(decimal monthlyAmount, int annualDiscountPercent)
    {
        var discount = Math.Clamp(annualDiscountPercent, 0, MaxAnnualDiscountPercent);
        return Math.Round(
            monthlyAmount * 12m * (100 - discount) / 100m,
            2,
            MidpointRounding.AwayFromZero);
    }
}
