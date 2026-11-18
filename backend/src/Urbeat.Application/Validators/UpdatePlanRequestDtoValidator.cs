using FluentValidation;
using Urbeat.Application.DTOs;
using Urbeat.Application.Plans;
using Urbeat.Domain.Entities;

namespace Urbeat.Application.Validators;

public sealed class UpdatePlanRequestDtoValidator : AbstractValidator<UpdatePlanRequestDto>
{
    public UpdatePlanRequestDtoValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(120);

        RuleFor(x => x.Description)
            .MaximumLength(500);

        RuleFor(x => x.TrialDays)
            .InclusiveBetween(0, Plan.MaxTrialDays)
            .When(x => x.TrialDays.HasValue);

        RuleFor(x => x.Benefits)
            .Must(benefits => PlanBenefits.TryNormalize(benefits, out _, out _))
            .WithMessage("Benefícios inválidos.");

        RuleFor(x => x.Level)
            .GreaterThanOrEqualTo(0)
            .When(x => x.Level.HasValue);

        RuleFor(x => x.AnnualDiscountPercent)
            .InclusiveBetween(0, Plan.MaxAnnualDiscountPercent)
            .When(x => x.AnnualDiscountPercent.HasValue);

        RuleFor(x => x)
            .Must(x => !(x.MonthlyEnabled == false && x.AnnualEnabled == false))
            .WithName(nameof(UpdatePlanRequestDto.MonthlyEnabled))
            .WithMessage("Habilite a cobrança mensal, a anual ou ambas.");

        RuleFor(x => x.EffectiveMonthlyAmount)
            .GreaterThan(0m)
            .WithName(nameof(UpdatePlanRequestDto.MonthlyAmount))
            .WithMessage("O valor mensal deve ser maior que zero.")
            .When(x => x.EffectiveMonthlyAmount.HasValue);
    }
}
