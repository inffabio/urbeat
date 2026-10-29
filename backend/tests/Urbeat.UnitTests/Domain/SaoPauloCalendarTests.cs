using FluentAssertions;
using Urbeat.Domain.Services;

namespace Urbeat.UnitTests.Domain;

public sealed class SaoPauloCalendarTests
{
    [Fact]
    public void GetLocalDate_ShouldReturnPreviousLocalDate_WhenUtcIsBeforeMidnight()
    {
        var utc = new DateTime(2026, 10, 29, 2, 30, 0, DateTimeKind.Utc);

        var localDate = SaoPauloCalendar.GetLocalDate(utc);

        localDate.Should().Be(new DateOnly(2026, 10, 28));
    }

    [Fact]
    public void GetLocalDate_ShouldUseSaoPauloOffset_ForDateTimeOffset()
    {
        var utc = new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

        SaoPauloCalendar.GetLocalDate(utc).Should().Be(new DateOnly(2026, 10, 29));
    }

    [Fact]
    public void StartOfDayUtc_ShouldReturn0300Z_ForRegularDay()
    {
        SaoPauloCalendar.StartOfDayUtc(new DateOnly(2026, 10, 29))
            .Should().Be(new DateTime(2026, 10, 29, 3, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void StartOfDayUtc_ShouldReturnFirstValidInstant_OnDstStart()
    {
        // 2018-11-04 00:00 does not exist; clocks jump to 01:00 (-02:00).
        SaoPauloCalendar.StartOfDayUtc(new DateOnly(2018, 11, 4))
            .Should().Be(new DateTime(2018, 11, 4, 3, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void EndOfDayExclusiveUtc_ShouldReturn0200Z_OnDstStart()
    {
        SaoPauloCalendar.EndOfDayExclusiveUtc(new DateOnly(2018, 11, 4))
            .Should().Be(new DateTime(2018, 11, 5, 2, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void StartOfDayUtc_ShouldUseDstOffset_On2019_02_16()
    {
        // Brazil was still on -02:00 on the morning of 2019-02-16.
        SaoPauloCalendar.StartOfDayUtc(new DateOnly(2019, 2, 16))
            .Should().Be(new DateTime(2019, 2, 16, 2, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void EndOfDayExclusiveUtc_ShouldIncludeTheRepeatedLateEvening_OnDstEnd()
    {
        // 2019-02-17 00:00 is unambiguous (-03:00), so the repeated 23:00-23:59
        // of 2019-02-16 belongs to 2019-02-16 and that day has 25 hours.
        SaoPauloCalendar.EndOfDayExclusiveUtc(new DateOnly(2019, 2, 16))
            .Should().Be(new DateTime(2019, 2, 17, 3, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void StartOfDayUtc_ShouldReturnNextDayStart_OnDstEnd()
    {
        SaoPauloCalendar.StartOfDayUtc(new DateOnly(2019, 2, 17))
            .Should().Be(new DateTime(2019, 2, 17, 3, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void AdjacentBoundaries_ShouldBeContiguous_OnDstEnd()
    {
        SaoPauloCalendar.EndOfDayExclusiveUtc(new DateOnly(2019, 2, 16))
            .Should().Be(SaoPauloCalendar.StartOfDayUtc(new DateOnly(2019, 2, 17)));
    }

    [Fact]
    public void StartOfMonthUtc_ShouldReturnDecemberFirst()
    {
        SaoPauloCalendar.StartOfMonthUtc(2026, 12)
            .Should().Be(new DateTime(2026, 12, 1, 3, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void EndOfMonthExclusiveUtc_ShouldCrossYear()
    {
        SaoPauloCalendar.EndOfMonthExclusiveUtc(2026, 12)
            .Should().Be(new DateTime(2027, 1, 1, 3, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void StartOfYearUtc_ShouldReturnJanuaryFirst()
    {
        SaoPauloCalendar.StartOfYearUtc(2026)
            .Should().Be(new DateTime(2026, 1, 1, 3, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void EndOfYearExclusiveUtc_ShouldReturnNextJanuaryFirst()
    {
        SaoPauloCalendar.EndOfYearExclusiveUtc(2026)
            .Should().Be(new DateTime(2027, 1, 1, 3, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void AllReturnedValues_ShouldBeUtcKind()
    {
        SaoPauloCalendar.StartOfDayUtc(new DateOnly(2026, 10, 29)).Kind.Should().Be(DateTimeKind.Utc);
        SaoPauloCalendar.EndOfDayExclusiveUtc(new DateOnly(2026, 10, 29)).Kind.Should().Be(DateTimeKind.Utc);
        SaoPauloCalendar.StartOfMonthUtc(2026, 10).Kind.Should().Be(DateTimeKind.Utc);
        SaoPauloCalendar.EndOfMonthExclusiveUtc(2026, 10).Kind.Should().Be(DateTimeKind.Utc);
        SaoPauloCalendar.StartOfYearUtc(2026).Kind.Should().Be(DateTimeKind.Utc);
        SaoPauloCalendar.EndOfYearExclusiveUtc(2026).Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void EndOfDayExclusiveUtc_ShouldReturnMaxValue_ForMaxDate()
    {
        SaoPauloCalendar.EndOfDayExclusiveUtc(DateOnly.MaxValue)
            .Should().Be(DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc));
    }

    [Fact]
    public void EndOfMonthExclusiveUtc_ShouldReturnMaxValue_ForMaxYearDecember()
    {
        SaoPauloCalendar.EndOfMonthExclusiveUtc(SaoPauloCalendar.MaxSupportedYear, 12)
            .Should().Be(DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc));
    }

    [Fact]
    public void EndOfYearExclusiveUtc_ShouldReturnMaxValue_ForMaxYear()
    {
        SaoPauloCalendar.EndOfYearExclusiveUtc(SaoPauloCalendar.MaxSupportedYear)
            .Should().Be(DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc));
    }

    [Fact]
    public void EndOfYearExclusiveUtc_ShouldRejectYearAfterMax()
    {
        var action = () => SaoPauloCalendar.EndOfYearExclusiveUtc(SaoPauloCalendar.MaxSupportedYear + 1);

        action.Should().Throw<ArgumentOutOfRangeException>();
    }
}
