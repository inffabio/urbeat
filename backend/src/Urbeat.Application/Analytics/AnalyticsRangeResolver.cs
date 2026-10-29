using System.Globalization;
using Urbeat.Application.DTOs;
using Urbeat.Domain.Services;

namespace Urbeat.Application.Analytics;

public static class AnalyticsRangeResolver
{
    private const string PeriodErrorMessage = "Período inválido.";
    private const string AnchorErrorMessage = "Data inválida.";
    private const string WeekBlockErrorMessage = "Bloco de semana inválido.";

    public static AnalyticsRangeResolution Resolve(StoreAnalyticsQueryDto query, DateOnly todayLocal)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        var granularity = ParseGranularity(query.Period);
        if (granularity is null)
        {
            errors["period"] = [PeriodErrorMessage];
        }

        var anchorParsed = TryParseAnchor(query.Anchor, todayLocal, out var anchor);
        if (!anchorParsed)
        {
            errors["anchor"] = [AnchorErrorMessage];
        }

        // weekBlock only makes sense with a valid week period and a valid anchor.
        if (granularity != AnalyticsGranularity.Week || !anchorParsed)
        {
            return errors.Count == 0
                ? AnalyticsRangeResolution.Success(BuildRange(granularity!.Value, anchor, null, todayLocal))
                : AnalyticsRangeResolution.Failure(errors);
        }

        var maxBlock = MaxWeekBlock(anchor.Year, anchor.Month);
        int block;
        if (query.WeekBlock.HasValue)
        {
            block = query.WeekBlock.Value;
            if (block is < 1 or > 5)
            {
                errors["weekBlock"] = [WeekBlockErrorMessage];
            }
            else if (block > maxBlock)
            {
                errors["weekBlock"] = [$"O mês não possui o bloco {block}."];
            }
        }
        else
        {
            block = Math.Min(((anchor.Day - 1) / 7) + 1, maxBlock);
        }

        if (errors.Count > 0)
        {
            return AnalyticsRangeResolution.Failure(errors);
        }

        return AnalyticsRangeResolution.Success(BuildRange(AnalyticsGranularity.Week, anchor, block, todayLocal));
    }

    private static AnalyticsGranularity? ParseGranularity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return AnalyticsGranularity.Day;
        }

        return value switch
        {
            "day" => AnalyticsGranularity.Day,
            "week" => AnalyticsGranularity.Week,
            "month" => AnalyticsGranularity.Month,
            "year" => AnalyticsGranularity.Year,
            _ => null
        };
    }

    private static bool TryParseAnchor(string? value, DateOnly todayLocal, out DateOnly anchor)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            anchor = todayLocal;
            return true;
        }

        return DateOnly.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out anchor);
    }

    private static AnalyticsRange BuildRange(
        AnalyticsGranularity period,
        DateOnly anchor,
        int? weekBlock,
        DateOnly todayLocal)
    {
        switch (period)
        {
            case AnalyticsGranularity.Day:
                return new AnalyticsRange(
                    period,
                    anchor,
                    null,
                    SaoPauloCalendar.StartOfDayUtc(anchor),
                    SaoPauloCalendar.EndOfDayExclusiveUtc(anchor),
                    anchor.Year);

            case AnalyticsGranularity.Week:
                var block = weekBlock ?? Math.Min(((anchor.Day - 1) / 7) + 1, MaxWeekBlock(anchor.Year, anchor.Month));
                var startDay = ((block - 1) * 7) + 1;
                var canonicalAnchor = new DateOnly(anchor.Year, anchor.Month, startDay);
                var maxBlock = MaxWeekBlock(anchor.Year, anchor.Month);
                var endDay = block >= maxBlock
                    ? DateTime.DaysInMonth(anchor.Year, anchor.Month)
                    : startDay + 6;
                var lastDay = new DateOnly(anchor.Year, anchor.Month, endDay);

                return new AnalyticsRange(
                    period,
                    canonicalAnchor,
                    block,
                    SaoPauloCalendar.StartOfDayUtc(canonicalAnchor),
                    SaoPauloCalendar.EndOfDayExclusiveUtc(lastDay),
                    canonicalAnchor.Year);

            case AnalyticsGranularity.Month:
                var monthAnchor = new DateOnly(anchor.Year, anchor.Month, 1);
                return new AnalyticsRange(
                    period,
                    monthAnchor,
                    null,
                    SaoPauloCalendar.StartOfMonthUtc(anchor.Year, anchor.Month),
                    SaoPauloCalendar.EndOfMonthExclusiveUtc(anchor.Year, anchor.Month),
                    monthAnchor.Year);

            case AnalyticsGranularity.Year:
                var yearAnchor = new DateOnly(anchor.Year, 1, 1);
                return new AnalyticsRange(
                    period,
                    yearAnchor,
                    null,
                    SaoPauloCalendar.StartOfYearUtc(anchor.Year),
                    SaoPauloCalendar.EndOfYearExclusiveUtc(anchor.Year),
                    yearAnchor.Year);

            default:
                // Unreachable because todayLocal is always valid; kept for exhaustive safety.
                return new AnalyticsRange(
                    AnalyticsGranularity.Day,
                    todayLocal,
                    null,
                    SaoPauloCalendar.StartOfDayUtc(todayLocal),
                    SaoPauloCalendar.EndOfDayExclusiveUtc(todayLocal),
                    todayLocal.Year);
        }
    }

    private static int MaxWeekBlock(int year, int month)
        => (DateTime.DaysInMonth(year, month) + 6) / 7;
}
