using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Application.Plans;
using Urbeat.Application.Validators;
using Urbeat.Domain.Entities;

namespace Urbeat.UnitTests.Application.Validators;

public sealed class ConfigurablePlanValidatorTests
{
    private readonly CreatePlanRequestDtoValidator _createValidator = new();
    private readonly UpdatePlanRequestDtoValidator _updateValidator = new();

    [Theory]
    [InlineData(false, false)]
    public void CreatePlan_ShouldRejectWhenNoBillingPeriodIsEnabled(bool monthlyEnabled, bool annualEnabled)
    {
        var result = _createValidator.Validate(new CreatePlanRequestDto
        {
            Name = "Sem período",
            MonthlyAmount = 19.90m,
            MonthlyEnabled = monthlyEnabled,
            AnnualEnabled = annualEnabled
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName.Contains("Enabled"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CreatePlan_ShouldRejectNonPositiveMonthlyAmount(decimal amount)
    {
        var result = _createValidator.Validate(new CreatePlanRequestDto
        {
            Name = "Preço inválido",
            MonthlyAmount = amount
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName.Contains("Monthly") || x.PropertyName.Contains("Amount"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void CreatePlan_ShouldRejectAnnualDiscountOutsideRange(int discount)
    {
        var result = _createValidator.Validate(new CreatePlanRequestDto
        {
            Name = "Desconto inválido",
            MonthlyAmount = 19.90m,
            AnnualEnabled = true,
            AnnualDiscountPercent = discount
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(CreatePlanRequestDto.AnnualDiscountPercent));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(366)]
    public void CreatePlan_ShouldRejectTrialOutsideAllowedRange(int trialDays)
    {
        var result = _createValidator.Validate(new CreatePlanRequestDto
        {
            Name = "Trial inválido",
            MonthlyAmount = 19.90m,
            TrialDays = trialDays
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(CreatePlanRequestDto.TrialDays));
    }

    [Fact]
    public void CreatePlan_ShouldRejectEmptyNameAndBlankBenefits()
    {
        var result = _createValidator.Validate(new CreatePlanRequestDto
        {
            Name = " ",
            MonthlyAmount = 19.90m,
            Benefits = ["Cardápio", "  "]
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Select(x => x.PropertyName)
            .Should()
            .Contain([nameof(CreatePlanRequestDto.Name), nameof(CreatePlanRequestDto.Benefits)]);
    }

    [Fact]
    public void CreatePlan_ShouldAcceptMonthlyOnlyPlan()
    {
        var result = _createValidator.Validate(new CreatePlanRequestDto
        {
            Name = "Começar",
            MonthlyAmount = 19.90m,
            MonthlyEnabled = true,
            AnnualEnabled = false,
            Description = "Plano de entrada"
        });

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors.Select(x => x.ErrorMessage)));
    }

    [Fact]
    public void CreatePlan_ShouldAcceptMonthlyAndAnnualPlanAndKeepLegacyAmount()
    {
        var result = _createValidator.Validate(new CreatePlanRequestDto
        {
            Name = "Crescer",
            Amount = 39.90m,
            MonthlyEnabled = true,
            AnnualEnabled = true,
            AnnualDiscountPercent = 15,
            Level = 2
        });

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors.Select(x => x.ErrorMessage)));
    }

    [Fact]
    public void UpdatePlan_ShouldPreserveOmittedPricingInValidation()
    {
        var result = _updateValidator.Validate(new UpdatePlanRequestDto
        {
            Name = "Crescer",
            Description = "Somente ordem alterada",
            Level = 3
        });

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors.Select(x => x.ErrorMessage)));
    }

    [Fact]
    public void UpdatePlan_ShouldStillRejectInvalidDiscountAndNoPeriod()
    {
        var result = _updateValidator.Validate(new UpdatePlanRequestDto
        {
            Name = "Crescer",
            MonthlyEnabled = false,
            AnnualEnabled = false,
            AnnualDiscountPercent = 120
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName.Contains("Enabled"));
        result.Errors.Should().Contain(x => x.PropertyName == nameof(UpdatePlanRequestDto.AnnualDiscountPercent));
    }

    [Fact]
    public void BillingPeriodConfigurationValidator_ShouldExposeFriendlyErrors()
    {
        var validator = new BillingPeriodConfigurationValidator();

        var result = validator.Validate(new PlanBillingPeriodConfiguration(
            MonthlyEnabled: false,
            AnnualEnabled: false,
            MonthlyAmount: 0,
            AnnualDiscountPercent: 200));

        result.IsValid.Should().BeFalse();
        result.Errors.Select(x => x.ErrorMessage).Should().Contain(message =>
            message.Contains("mensal", StringComparison.OrdinalIgnoreCase)
            || message.Contains("anual", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validator_ShouldUseMonthlyAmountOverLegacyAmount()
    {
        var request = new CreatePlanRequestDto
        {
            Name = "Preço autoritativo",
            Amount = 99.90m,
            MonthlyAmount = 49.90m
        };

        request.EffectiveMonthlyAmount.Should().Be(49.90m);

        var plan = new Plan
        {
            Name = "Preço autoritativo",
            Amount = 49.90m,
            AnnualEnabled = true,
            AnnualDiscountPercent = 10
        };

        plan.AnnualAmount.Should().Be(Plan.CalculateAnnualAmount(49.90m, 10));
    }
}
