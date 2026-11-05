namespace Urbeat.Application.DTOs;

public sealed class UpdatePlanRequestDto
{
    public string Name { get; init; } = string.Empty;

    public decimal Amount { get; init; }

    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Ausente preserva o trial atual do plano.
    /// </summary>
    public int? TrialDays { get; init; }

    /// <summary>
    /// Ausente preserva os benefícios atuais; uma lista vazia remove os benefícios.
    /// </summary>
    public IReadOnlyList<string>? Benefits { get; init; }
}
