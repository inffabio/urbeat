namespace Urbeat.Application.DTOs;

public sealed class CreatePlanRequestDto
{
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Preço mensal autoritativo. Quando informado, prevalece sobre o campo
    /// legado <see cref="Amount"/>. O valor anual é calculado pelo servidor a
    /// partir deste preço e do desconto configurado.
    /// </summary>
    public decimal? MonthlyAmount { get; init; }

    /// <summary>
    /// Campo legado de compatibilidade (preço mensal). Continua aceito para
    /// clientes antigos; nunca sobrepõe <see cref="MonthlyAmount"/>.
    /// </summary>
    public decimal? Amount { get; init; }

    public string Description { get; init; } = string.Empty;

    public bool IsActive { get; init; } = true;

    /// <summary>
    /// Dias de teste gratuito do plano. Ausente usa o padrão do sistema.
    /// </summary>
    public int? TrialDays { get; init; }

    /// <summary>
    /// Benefícios informativos. Ausente cria o plano sem benefícios.
    /// </summary>
    public IReadOnlyList<string>? Benefits { get; init; }

    public bool? MonthlyEnabled { get; init; }

    public bool? AnnualEnabled { get; init; }

    public int? AnnualDiscountPercent { get; init; }

    /// <summary>Nível administrativo do plano (maior = superior).</summary>
    public int? Level { get; init; }

    public decimal EffectiveMonthlyAmount => MonthlyAmount ?? Amount ?? 0m;

    public bool EffectiveMonthlyEnabled => MonthlyEnabled ?? true;

    public bool EffectiveAnnualEnabled => AnnualEnabled ?? false;

    public int EffectiveAnnualDiscountPercent => AnnualDiscountPercent ?? 0;

    public int EffectiveLevel => Level ?? 0;
}
