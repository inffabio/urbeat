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
