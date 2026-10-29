namespace Urbeat.Domain.Services;

/// <summary>
/// Single source of truth for the Sao Paulo (America/Sao_Paulo) business calendar.
/// Boundaries are resolved with <see cref="TimeZoneInfo"/> so historical daylight-saving
/// transitions are respected; never use a fixed -03:00 offset.
/// </summary>
public static class SaoPauloCalendar
{
    public const string TimeZoneId = "America/Sao_Paulo";

    /// <summary>
    /// Highest calendar year supported by the analytics series. Year 9999 is supported; its
    /// exclusive upper bound is represented by <see cref="DateTime.MaxValue"/> because year
    /// 10000 does not exist.
    /// </summary>
    public const int MaxSupportedYear = 9999;

    private const int MaxInvalidTimeScanMinutes = 180;

    private static readonly DateTime MaxUtc = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);

    private static readonly Lazy<TimeZoneInfo> LazyTimeZone = new(ResolveTimeZone, LazyThreadSafetyMode.ExecutionAndPublication);

    public static TimeZoneInfo TimeZone => LazyTimeZone.Value;

    public static DateOnly GetLocalDate(DateTimeOffset utcNow)
    {
        var local = TimeZoneInfo.ConvertTime(utcNow.ToUniversalTime(), TimeZone);
        return DateOnly.FromDateTime(local.DateTime);
    }

    public static DateOnly GetLocalDate(DateTime utcNow)
    {
        var utc = utcNow.Kind switch
        {
            DateTimeKind.Utc => utcNow,
            DateTimeKind.Local => utcNow.ToUniversalTime(),
            _ => DateTime.SpecifyKind(utcNow, DateTimeKind.Utc)
        };

        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZone).Date);
    }

    /// <summary>
    /// First instant of the local day in UTC. When local midnight does not exist (DST start),
    /// the first valid local instant of that day is used.
    /// </summary>
    public static DateTime StartOfDayUtc(DateOnly date)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);

        if (TimeZone.IsInvalidTime(local))
        {
            local = FindFirstValidLocalInstant(local);
        }

        var utc = TimeZone.IsAmbiguousTime(local)
            ? ToUtcUsingEarliestInstant(local)
            : TimeZoneInfo.ConvertTimeToUtc(local, TimeZone);

        return DateTime.SpecifyKind(utc, DateTimeKind.Utc);
    }

    /// <summary>
    /// Exclusive upper bound of the local day. Always equal to the next day's start so
    /// adjacent ranges never overlap and never leave a gap across a DST transition.
    /// When clocks fall back at local midnight, the repeated late evening belongs to the
    /// previous local day (for example 2019-02-16 in Sao Paulo has 25 hours).
    /// </summary>
    public static DateTime EndOfDayExclusiveUtc(DateOnly date)
    {
        if (date == DateOnly.MaxValue)
        {
            return MaxUtc;
        }

        return StartOfDayUtc(date.AddDays(1));
    }

    public static DateTime StartOfMonthUtc(int year, int month)
        => StartOfDayUtc(new DateOnly(year, month, 1));

    public static DateTime EndOfMonthExclusiveUtc(int year, int month)
    {
        if (year == MaxSupportedYear && month == 12)
        {
            return MaxUtc;
        }

        var nextMonth = month == 12 ? new DateOnly(year + 1, 1, 1) : new DateOnly(year, month + 1, 1);
        return StartOfDayUtc(nextMonth);
    }

    public static DateTime StartOfYearUtc(int year)
        => StartOfDayUtc(new DateOnly(year, 1, 1));

    public static DateTime EndOfYearExclusiveUtc(int year)
    {
        if (year == MaxSupportedYear)
        {
            return MaxUtc;
        }

        if (year > MaxSupportedYear)
        {
            throw new ArgumentOutOfRangeException(nameof(year), "Years after 9999 cannot be represented.");
        }

        return StartOfDayUtc(new DateOnly(year + 1, 1, 1));
    }

    /// <summary>
    /// When a local time is ambiguous (DST end), pick the earliest UTC instant, which
    /// corresponds to the largest (daylight) offset.
    /// </summary>
    private static DateTime ToUtcUsingEarliestInstant(DateTime local)
    {
        var offsets = TimeZone.GetAmbiguousTimeOffsets(local);
        var offset = offsets.Max();
        return DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);
    }

    private static DateTime FindFirstValidLocalInstant(DateTime invalidLocal)
    {
        var candidate = invalidLocal;
        for (var minute = 0; minute <= MaxInvalidTimeScanMinutes && TimeZone.IsInvalidTime(candidate); minute++)
        {
            candidate = candidate.AddMinutes(1);
        }

        return candidate;
    }

    private static TimeZoneInfo ResolveTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");
        }
    }
}
