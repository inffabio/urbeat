using FluentAssertions;
using Urbeat.Domain.Entities;
using Urbeat.Domain.Services;

namespace Urbeat.UnitTests.Domain;

public sealed class StoreOpeningHoursCalculatorTests
{
    [Fact]
    public void Calculate_ShouldBeOpen_WhenCurrentTimeIsInsideCurrentDayShift()
    {
        var now = new DateTimeOffset(2026, 7, 28, 22, 0, 0, TimeSpan.Zero);
        var hours = new[]
        {
            CreateHour(DayOfWeek.Tuesday, true, new TimeOnly(18, 0), new TimeOnly(23, 0))
        };

        var result = StoreOpeningHoursCalculator.Calculate(true, hours, now);

        result.IsOpenNow.Should().BeTrue();
        result.ClosedMessage.Should().BeNull();
        result.NextStatusChangeAtUtc.Should().Be(new DateTimeOffset(2026, 7, 29, 2, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Calculate_ShouldReturnNextOpeningMessage_WhenStoreIsClosedBeforeShift()
    {
        var now = new DateTimeOffset(2026, 7, 28, 15, 0, 0, TimeSpan.Zero);
        var hours = new[]
        {
            CreateHour(DayOfWeek.Tuesday, true, new TimeOnly(18, 0), new TimeOnly(23, 0))
        };

        var result = StoreOpeningHoursCalculator.Calculate(true, hours, now);

        result.IsOpenNow.Should().BeFalse();
        result.ClosedMessage.Should().Be("A loja só estará aberta Terça às 18:00.");
        result.NextStatusChangeAtUtc.Should().Be(new DateTimeOffset(2026, 7, 28, 21, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Calculate_ShouldBeOpen_WhenCurrentTimeIsInsidePreviousDayOvernightShift()
    {
        var now = new DateTimeOffset(2026, 7, 29, 4, 30, 0, TimeSpan.Zero);
        var hours = new[]
        {
            CreateHour(DayOfWeek.Tuesday, true, new TimeOnly(22, 0), new TimeOnly(2, 0))
        };

        var result = StoreOpeningHoursCalculator.Calculate(true, hours, now);

        result.IsOpenNow.Should().BeTrue();
        result.NextStatusChangeAtUtc.Should().Be(new DateTimeOffset(2026, 7, 29, 5, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Calculate_ShouldRemainClosed_WhenManualStoreStatusIsClosed()
    {
        var now = new DateTimeOffset(2026, 7, 28, 22, 0, 0, TimeSpan.Zero);
        var hours = new[]
        {
            CreateHour(DayOfWeek.Tuesday, true, new TimeOnly(18, 0), new TimeOnly(23, 0))
        };

        var result = StoreOpeningHoursCalculator.Calculate(false, hours, now);

        result.IsOpenNow.Should().BeFalse();
        result.ClosedMessage.Should().Be("A loja está fechada no momento.");
    }

    [Fact]
    public void GetSaoPauloDate_ShouldStayOnPreviousDay_BeforeSaoPauloMidnight()
    {
        // 02:30 UTC is 23:30 on the previous day in Sao Paulo (UTC-3).
        var utcNow = new DateTimeOffset(2026, 1, 2, 2, 30, 0, TimeSpan.Zero);

        StoreOpeningHoursCalculator.GetSaoPauloDate(utcNow).Should().Be(new DateOnly(2026, 1, 1));
    }

    [Fact]
    public void GetSaoPauloDate_ShouldRollToNextDay_AtSaoPauloMidnight()
    {
        // 03:00 UTC is exactly midnight in Sao Paulo (UTC-3), starting the new local day.
        var utcNow = new DateTimeOffset(2026, 1, 2, 3, 0, 0, TimeSpan.Zero);

        StoreOpeningHoursCalculator.GetSaoPauloDate(utcNow).Should().Be(new DateOnly(2026, 1, 2));
    }

    [Fact]
    public void GetSaoPauloDate_ShouldNormalizeInputOffsetToUtc()
    {
        // 03:00 at +03:00 equals 00:00 UTC, which is still 21:00 of the previous day in Sao Paulo.
        var offsetNow = new DateTimeOffset(2026, 1, 2, 3, 0, 0, TimeSpan.FromHours(3));

        StoreOpeningHoursCalculator.GetSaoPauloDate(offsetNow).Should().Be(new DateOnly(2026, 1, 1));
    }

    private static StoreBusinessHour CreateHour(DayOfWeek day, bool isOpen, TimeOnly start, TimeOnly end)
    {
        return new StoreBusinessHour
        {
            DayOfWeek = day,
            IsOpen = isOpen,
            Shifts =
            [
                new StoreBusinessHourShift
                {
                    StartTime = start,
                    EndTime = end
                }
            ]
        };
    }
}
