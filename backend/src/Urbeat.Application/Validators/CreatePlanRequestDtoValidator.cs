using FluentValidation;
using Urbeat.Application.DTOs;
using Urbeat.Application.Plans;
using Urbeat.Domain.Entities;

namespace Urbeat.Application.Validators;

public sealed class CreatePlanRequestDtoValidator : AbstractValidator<CreatePlanRequestDto>
{
    public CreatePlanRequestDtoValidator()
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

        RuleFor(x => x)
            .Custom((request, context) =>
            {
                var configuration = new PlanBillingPeriodConfiguration(
                    request.EffectiveMonthlyEnabled,
                    request.EffectiveAnnualEnabled,
                    request.EffectiveMonthlyAmount,
                    request.EffectiveAnnualDiscountPercent);

                var validation = new BillingPeriodConfigurationValidator().Validate(configuration);
                foreach (var failure in validation.Errors)
                {
                    context.AddFailure(failure.PropertyName, failure.ErrorMessage);
                }
            });
    }
}
