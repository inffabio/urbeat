namespace Urbeat.Application.DTOs;

public sealed class UpdatePlanRequestDto
{
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Preço mensal autoritativo. Quando omitido, o preço atual é preservado.
    /// Quando informado, prevalece sobre o campo legado <see cref="Amount"/>.
    /// </summary>
    public decimal? MonthlyAmount { get; init; }

    /// <summary>
    /// Campo legado de compatibilidade (preço mensal). Continua aceito para
    /// clientes antigos; nunca sobrepõe <see cref="MonthlyAmount"/>.
    /// </summary>
    public decimal? Amount { get; init; }

    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Ausente preserva o trial atual do plano.
    /// </summary>
    public int? TrialDays { get; init; }

    /// <summary>
    /// Ausente preserva os benefícios atuais; uma lista vazia remove os benefícios.
    /// </summary>
    public IReadOnlyList<string>? Benefits { get; init; }

    /// <summary>Ausente preserva a configuração atual de cobrança mensal.</summary>
    public bool? MonthlyEnabled { get; init; }

    /// <summary>Ausente preserva a configuração atual de cobrança anual.</summary>
    public bool? AnnualEnabled { get; init; }

    /// <summary>Ausente preserva o desconto atual.</summary>
    public int? AnnualDiscountPercent { get; init; }

    /// <summary>Ausente preserva o nível atual.</summary>
    public int? Level { get; init; }

    public decimal? EffectiveMonthlyAmount => MonthlyAmount ?? (Amount is > 0m ? Amount : null);
}
