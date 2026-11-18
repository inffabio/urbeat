using FluentValidation;
using Urbeat.Application.Plans;
using Urbeat.Domain.Entities;

namespace Urbeat.Application.Validators;

public sealed class BillingPeriodConfigurationValidator : AbstractValidator<PlanBillingPeriodConfiguration>
{
    public BillingPeriodConfigurationValidator()
    {
        RuleFor(x => x)
            .Must(x => x.MonthlyEnabled || x.AnnualEnabled)
            .WithName(nameof(PlanBillingPeriodConfiguration.MonthlyEnabled))
            .WithMessage("Habilite a cobrança mensal, a anual ou ambas.");

        RuleFor(x => x.MonthlyAmount)
            .GreaterThan(0m)
            .WithName(nameof(PlanBillingPeriodConfiguration.MonthlyAmount))
            .WithMessage("O valor mensal deve ser maior que zero.");

        RuleFor(x => x.AnnualDiscountPercent)
            .InclusiveBetween(0, Plan.MaxAnnualDiscountPercent)
            .WithName(nameof(PlanBillingPeriodConfiguration.AnnualDiscountPercent))
            .WithMessage($"O desconto anual deve estar entre 0 e {Plan.MaxAnnualDiscountPercent}%.");
    }
}
