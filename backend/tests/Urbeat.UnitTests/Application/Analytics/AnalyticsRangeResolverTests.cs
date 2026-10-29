using FluentAssertions;
using Urbeat.Application.Analytics;
using Urbeat.Application.DTOs;
using Urbeat.Domain.Services;

namespace Urbeat.UnitTests.Application.Analytics;

public sealed class AnalyticsRangeResolverTests
{
    private static readonly DateOnly TodayLocal = new(2026, 10, 29);

    [Fact]
    public void Resolve_ShouldReturnDayRange()
    {
        var resolution = Resolve(new StoreAnalyticsQueryDto { Period = "day", Anchor = "2026-10-29" });

        resolution.IsValid.Should().BeTrue();
        resolution.Range!.Period.Should().Be(AnalyticsGranularity.Day);
        resolution.Range.AnchorDate.Should().Be(new DateOnly(2026, 10, 29));
        resolution.Range.WeekBlock.Should().BeNull();
        resolution.Range.StartUtc.Should().Be(Utc(2026, 10, 29, 3));
        resolution.Range.EndUtcExclusive.Should().Be(Utc(2026, 10, 30, 3));
        resolution.Range.ChartYear.Should().Be(2026);
    }

    [Theory]
    [InlineData(1, 1, 8)]
    [InlineData(2, 8, 15)]
    [InlineData(3, 15, 22)]
    [InlineData(4, 22, 29)]
    public void Resolve_ShouldReturnWeekBlocksOneToFour(int block, int startDay, int endDay)
    {
        var resolution = Resolve(new StoreAnalyticsQueryDto
        {
            Period = "week",
            Anchor = $"2026-10-{startDay:D2}",
            WeekBlock = block
        });

        resolution.IsValid.Should().BeTrue();
        resolution.Range!.AnchorDate.Should().Be(new DateOnly(2026, 10, startDay));
        resolution.Range.StartUtc.Should().Be(Utc(2026, 10, startDay, 3));
        resolution.Range.EndUtcExclusive.Should().Be(Utc(2026, 10, endDay, 3));
        resolution.Range.WeekBlock.Should().Be(block);
    }

    [Fact]
    public void Resolve_ShouldReturnWeekBlockFive_InOctober()
    {
        var resolution = Resolve(new StoreAnalyticsQueryDto
        {
            Period = "week",
            Anchor = "2026-10-29",
            WeekBlock = 5
        });

        resolution.IsValid.Should().BeTrue();
        resolution.Range!.StartUtc.Should().Be(Utc(2026, 10, 29, 3));
        resolution.Range.EndUtcExclusive.Should().Be(Utc(2026, 11, 1, 3));
        resolution.Range.AnchorDate.Should().Be(new DateOnly(2026, 10, 29));
    }

    [Fact]
    public void Resolve_ShouldRejectBlockFive_WhenFebruaryHasTwentyEightDays()
    {
        var resolution = Resolve(new StoreAnalyticsQueryDto
        {
            Period = "week",
            Anchor = "2026-02-10",
            WeekBlock = 5
        });

        resolution.IsValid.Should().BeFalse();
        resolution.Errors.Should().ContainKey("weekBlock");
        resolution.Errors["weekBlock"].Should().ContainSingle().Which.Should().Be("O mês não possui o bloco 5.");
    }

    [Fact]
    public void Resolve_ShouldAllowBlockFive_WhenFebruaryIsLeap()
    {
        var resolution = Resolve(new StoreAnalyticsQueryDto
        {
            Period = "week",
            Anchor = "2028-02-29",
            WeekBlock = 5
        });

        resolution.IsValid.Should().BeTrue();
        resolution.Range!.StartUtc.Should().Be(Utc(2028, 2, 29, 3));
        resolution.Range.EndUtcExclusive.Should().Be(Utc(2028, 3, 1, 3));
    }

    [Fact]
    public void Resolve_ShouldReturnMonthRange()
    {
        var resolution = Resolve(new StoreAnalyticsQueryDto { Period = "month", Anchor = "2026-12-17" });

        resolution.IsValid.Should().BeTrue();
        resolution.Range!.AnchorDate.Should().Be(new DateOnly(2026, 12, 1));
        resolution.Range.StartUtc.Should().Be(Utc(2026, 12, 1, 3));
        resolution.Range.EndUtcExclusive.Should().Be(Utc(2027, 1, 1, 3));
        resolution.Range.ChartYear.Should().Be(2026);
    }

    [Fact]
    public void Resolve_ShouldReturnYearRange()
    {
        var resolution = Resolve(new StoreAnalyticsQueryDto { Period = "year", Anchor = "2026-07-19" });

        resolution.IsValid.Should().BeTrue();
        resolution.Range!.AnchorDate.Should().Be(new DateOnly(2026, 1, 1));
        resolution.Range.StartUtc.Should().Be(Utc(2026, 1, 1, 3));
        resolution.Range.EndUtcExclusive.Should().Be(Utc(2027, 1, 1, 3));
    }

    [Fact]
    public void Resolve_ShouldHandleDstStartDay()
    {
        var resolution = Resolve(new StoreAnalyticsQueryDto { Period = "day", Anchor = "2018-11-04" });

        resolution.Range!.StartUtc.Should().Be(Utc(2018, 11, 4, 3));
        resolution.Range.EndUtcExclusive.Should().Be(Utc(2018, 11, 5, 2));
    }

    [Fact]
    public void Resolve_ShouldHandleDstEndDay()
    {
        var resolution = Resolve(new StoreAnalyticsQueryDto { Period = "day", Anchor = "2019-02-16" });

        resolution.Range!.StartUtc.Should().Be(Utc(2019, 2, 16, 2));
        resolution.Range.EndUtcExclusive.Should().Be(Utc(2019, 2, 17, 3));
    }

    [Theory]
    [InlineData("2026-10-29", 5)]
    [InlineData("2026-10-28", 4)]
    [InlineData("2026-10-31", 5)]
    [InlineData("2026-02-28", 4)]
    public void Resolve_ShouldDeriveWeekBlockFromAnchorDay_WhenOmitted(string anchor, int expectedBlock)
    {
        var resolution = Resolve(new StoreAnalyticsQueryDto { Period = "week", Anchor = anchor });

        resolution.IsValid.Should().BeTrue();
        resolution.Range!.WeekBlock.Should().Be(expectedBlock);
    }

    [Fact]
    public void Resolve_ShouldNormalizeWeekAnchorToBlockStart()
    {
        var resolution = Resolve(new StoreAnalyticsQueryDto
        {
            Period = "week",
            Anchor = "2026-10-30",
            WeekBlock = 5
        });

        resolution.Range!.AnchorDate.Should().Be(new DateOnly(2026, 10, 29));
    }

    [Fact]
    public void Resolve_ShouldNormalizeMonthAndYearAnchors()
    {
        Resolve(new StoreAnalyticsQueryDto { Period = "month", Anchor = "2026-10-30" })
            .Range!.AnchorDate.Should().Be(new DateOnly(2026, 10, 1));

        Resolve(new StoreAnalyticsQueryDto { Period = "year", Anchor = "2026-10-30" })
            .Range!.AnchorDate.Should().Be(new DateOnly(2026, 1, 1));
    }

    [Fact]
    public void Resolve_ShouldReportPeriodAndAnchorErrorsIndependently()
    {
        var resolution = Resolve(new StoreAnalyticsQueryDto { Period = "quarter", Anchor = "2026-02-30" });

        resolution.IsValid.Should().BeFalse();
        resolution.Errors.Should().ContainKey("period");
        resolution.Errors["period"].Should().ContainSingle().Which.Should().Be("Período inválido.");
        resolution.Errors.Should().ContainKey("anchor");
        resolution.Errors["anchor"].Should().ContainSingle().Which.Should().Be("Data inválida.");
    }

    [Fact]
    public void Resolve_ShouldIgnoreWeekBlock_OutsideWeekPeriod()
    {
        var resolution = Resolve(new StoreAnalyticsQueryDto { Period = "day", Anchor = "2026-10-29", WeekBlock = 9 });

        resolution.IsValid.Should().BeTrue();
        resolution.Range!.WeekBlock.Should().BeNull();
    }

    [Fact]
    public void Resolve_ShouldRejectWeekBlockZero()
    {
        var resolution = Resolve(new StoreAnalyticsQueryDto { Period = "week", Anchor = "2026-10-29", WeekBlock = 0 });

        resolution.IsValid.Should().BeFalse();
        resolution.Errors["weekBlock"].Should().ContainSingle().Which.Should().Be("Bloco de semana inválido.");
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    public void Resolve_ShouldDefaultToToday_WhenPeriodAndAnchorAreMissing(string? period, string? anchor)
    {
        var resolution = Resolve(new StoreAnalyticsQueryDto { Period = period, Anchor = anchor });

        resolution.IsValid.Should().BeTrue();
        resolution.Range!.Period.Should().Be(AnalyticsGranularity.Day);
        resolution.Range.AnchorDate.Should().Be(TodayLocal);
        resolution.Range.StartUtc.Should().Be(Utc(2026, 10, 29, 3));
    }

    [Theory]
    [InlineData("day", "9999-12-31")]
    [InlineData("month", "9999-12-15")]
    [InlineData("year", "9999-01-01")]
    [InlineData("week", "9999-12-29")]
    public void Resolve_ShouldSupportYear9999_UsingMaxValueAsExclusiveBoundary(string period, string anchor)
    {
        var resolution = Resolve(new StoreAnalyticsQueryDto { Period = period, Anchor = anchor, WeekBlock = 5 });

        resolution.IsValid.Should().BeTrue();
        resolution.Range!.ChartYear.Should().Be(9999);
        resolution.Range.EndUtcExclusive.Should().Be(DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc));
    }

    [Fact]
    public void Resolve_ShouldSupportDefaultToday_AtYear9999()
    {
        var resolution = AnalyticsRangeResolver.Resolve(
            new StoreAnalyticsQueryDto { Period = "day" },
            new DateOnly(SaoPauloCalendar.MaxSupportedYear, 12, 31));

        resolution.IsValid.Should().BeTrue();
        resolution.Range!.AnchorDate.Should().Be(DateOnly.MaxValue);
        resolution.Range.EndUtcExclusive.Should().Be(DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc));
    }

    [Theory]
    [InlineData("day", "9999-12-30")]
    [InlineData("month", "9999-11-01")]
    [InlineData("year", "9998-01-01")]
    public void Resolve_ShouldAcceptAnchorUpToTheLastSupportedYear(string period, string anchor)
    {
        var resolution = Resolve(new StoreAnalyticsQueryDto { Period = period, Anchor = anchor });

        resolution.IsValid.Should().BeTrue();
        resolution.Range!.ChartYear.Should().BeLessThanOrEqualTo(SaoPauloCalendar.MaxSupportedYear);
    }

    private static AnalyticsRangeResolution Resolve(StoreAnalyticsQueryDto query)
        => AnalyticsRangeResolver.Resolve(query, TodayLocal);

    private static DateTime Utc(int year, int month, int day, int hour)
        => new(year, month, day, hour, 0, 0, DateTimeKind.Utc);
}
