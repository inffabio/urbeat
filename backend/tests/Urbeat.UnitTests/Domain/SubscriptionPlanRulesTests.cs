using FluentAssertions;
using Urbeat.Domain.Entities;
using Urbeat.Domain.Services;

namespace Urbeat.UnitTests.Domain;

public sealed class SubscriptionPlanRulesTests
{
    [Fact]
    public void Plan_ShouldDefaultToMonthlyOnlyWithoutDiscount()
    {
        var plan = new Plan { Name = "Começar", Amount = 19.90m };

        plan.MonthlyEnabled.Should().BeTrue();
        plan.AnnualEnabled.Should().BeFalse();
        plan.AnnualDiscountPercent.Should().Be(0);
        plan.Level.Should().Be(0);
        plan.MonthlyAmount.Should().Be(19.90m);
        plan.AnnualAmount.Should().Be(238.80m);
    }

    [Fact]
    public void MonthlyAmount_ShouldRemainAliasOfAuthoritativeMonthlyPrice()
    {
        var plan = new Plan { Name = "Crescer", Amount = 10m };

        plan.MonthlyAmount = 49.90m;

        plan.Amount.Should().Be(49.90m);
        plan.MonthlyAmount.Should().Be(49.90m);
    }

    [Fact]
    public void AnnualAmount_ShouldApplyDiscountAndRoundToCents()
    {
        var plan = new Plan
        {
            Name = "Profissional",
            Amount = 79.90m,
            AnnualEnabled = true,
            AnnualDiscountPercent = 20
        };

        plan.AnnualAmount.Should().Be(767.04m);
    }

    [Fact]
    public void Supports_ShouldRespectEnabledPeriods()
    {
        var plan = new Plan
        {
            Name = "Anual",
            Amount = 39.90m,
            MonthlyEnabled = false,
            AnnualEnabled = true
        };

        plan.Supports(SubscriptionBillingPeriod.Monthly).Should().BeFalse();
        plan.Supports(SubscriptionBillingPeriod.Annual).Should().BeTrue();
        plan.GetAmountFor(SubscriptionBillingPeriod.Annual).Should().Be(478.80m);
    }

    [Fact]
    public void GetAmountForPeriod_ShouldRejectDisabledPeriod()
    {
        var plan = new Plan { Name = "Mensal", Amount = 39.90m };

        var act = () => plan.GetAmountFor(SubscriptionBillingPeriod.Annual);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void IsSuperiorTo_ShouldUseConfiguredLevelInsteadOfPrice()
    {
        var cheaperButHigher = new Plan { Name = "Premium", Amount = 10m, Level = 3 };
        var expensiveButLower = new Plan { Name = "Básico", Amount = 500m, Level = 1 };

        cheaperButHigher.IsSuperiorTo(expensiveButLower).Should().BeTrue();
        expensiveButLower.IsSuperiorTo(cheaperButHigher).Should().BeFalse();
    }

    [Fact]
    public void CalculateFirstChargeDueUtc_ShouldStartAtTrialEndOrImmediately()
    {
        var start = new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);

        SubscriptionBillingCalculator
            .CalculateFirstChargeDueUtc(15, start, start)
            .Should()
            .Be(start.AddDays(15));

        SubscriptionBillingCalculator
            .CalculateFirstChargeDueUtc(0, start, start)
            .Should()
            .Be(start);

        SubscriptionBillingCalculator
            .CalculateFirstChargeDueUtc(15, start, start.AddDays(30))
            .Should()
            .Be(start.AddDays(30));
    }

    [Fact]
    public void CalculatePeriodEndUtc_ShouldUseMonthlyAndAnnualWindows()
    {
        var start = new DateTime(2026, 11, 18, 0, 0, 0, DateTimeKind.Utc);

        SubscriptionBillingCalculator
            .CalculatePeriodEndUtc(start, SubscriptionBillingPeriod.Monthly)
            .Should()
            .Be(new DateTime(2026, 12, 18, 0, 0, 0, DateTimeKind.Utc));

        SubscriptionBillingCalculator
            .CalculatePeriodEndUtc(start, SubscriptionBillingPeriod.Annual)
            .Should()
            .Be(new DateTime(2027, 11, 18, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void SellerSubscription_ShouldPersistCommercialSnapshot()
    {
        var start = new DateTime(2026, 11, 18, 0, 0, 0, DateTimeKind.Utc);
        var subscription = new SellerSubscription
        {
            StoreId = Guid.NewGuid(),
            SellerUserId = Guid.NewGuid(),
            PlanName = "Crescer",
            PlanDescriptionSnapshot = "Plano intermediário",
            PlanAmount = 383.04m,
            BillingPeriod = SubscriptionBillingPeriod.Annual,
            TrialDaysSnapshot = 15,
            AnnualDiscountPercentSnapshot = 20,
            PlanLevelSnapshot = 2,
            StartDateUtc = start,
            TrialEndsAtUtc = start.AddDays(15),
            NextBillingDateUtc = start.AddDays(15)
        };

        subscription.BillingPeriod.Should().Be(SubscriptionBillingPeriod.Annual);
        subscription.PlanDescriptionSnapshot.Should().Be("Plano intermediário");
        subscription.TrialDaysSnapshot.Should().Be(15);
        subscription.AnnualDiscountPercentSnapshot.Should().Be(20);
        subscription.PlanLevelSnapshot.Should().Be(2);
        subscription.StartDateUtc.Kind.Should().Be(DateTimeKind.Utc);
        subscription.TrialEndsAtUtc.Should().Be(start.AddDays(15));
    }
}
