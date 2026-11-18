namespace Urbeat.Application.Plans;

/// <summary>
/// Configuração de cobrança de um plano, isolada para validação compartilhada
/// entre criação e edição administrativa.
/// </summary>
public sealed record PlanBillingPeriodConfiguration(
    bool MonthlyEnabled,
    bool AnnualEnabled,
    decimal MonthlyAmount,
    int AnnualDiscountPercent);
