namespace Urbeat.Application.DTOs;

public sealed class CreatePlanRequestDto
{
    public string Name { get; init; } = string.Empty;

    public decimal Amount { get; init; }

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
}
