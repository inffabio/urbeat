namespace Urbeat.Domain.Entities;

public sealed class Plan : BaseEntity
{
    /// <summary>
    /// Dias de teste gratuito aplicados a novas lojas/assinaturas deste plano.
    /// O trial é por plano (não global); zero significa cobrança já no próximo ciclo.
    /// </summary>
    public const int DefaultTrialDays = 15;

    public const int MaxTrialDays = 365;

    public string Name { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public string Description { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public int TrialDays { get; set; } = DefaultTrialDays;

    /// <summary>
    /// Benefícios informativos do plano serializados como JSON (array de strings).
    /// Não representa autorização de recursos; a autorização futura deve ser feita no servidor.
    /// </summary>
    public string BenefitsJson { get; set; } = "[]";
}
