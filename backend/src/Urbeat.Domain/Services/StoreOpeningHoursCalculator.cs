using Urbeat.Domain.Entities;

namespace Urbeat.Domain.Services;

public static class StoreOpeningHoursCalculator
{
    private const int MinutesPerDay = 24 * 60;

    private static readonly TimeZoneInfo SaoPauloTimeZone = ResolveSaoPauloTimeZone();

    public static StoreOpeningHoursStatus Calculate(
        bool isStoreEnabled,
        IEnumerable<StoreBusinessHour> businessHours,
        DateTimeOffset utcNow)
    {
        if (!isStoreEnabled)
        {
            return new StoreOpeningHoursStatus(false, null, null, "A loja está fechada no momento.");
        }

        var hours = businessHours.ToList();
        if (hours.Count == 0)
        {
            return new StoreOpeningHoursStatus(true, null, null, null);
        }

        var localNow = TimeZoneInfo.ConvertTime(utcNow.ToUniversalTime(), SaoPauloTimeZone);
        if (IsOpenAt(hours, localNow))
        {
            var nextClosing = FindNextClosing(hours, localNow);
            return new StoreOpeningHoursStatus(true, null, ToUtc(nextClosing), null);
        }

        var nextOpening = FindNextOpening(hours, localNow);
        if (nextOpening is null)
        {
            return new StoreOpeningHoursStatus(false, null, null, "A loja está fechada no momento.");
        }

        var message = $"A loja só estará aberta {FormatDay(nextOpening.Value.DayOfWeek)} às {nextOpening.Value:HH:mm}.";
        return new StoreOpeningHoursStatus(false, ToUtc(nextOpening), ToUtc(nextOpening), message);
    }

    private static bool IsOpenAt(IReadOnlyCollection<StoreBusinessHour> hours, DateTimeOffset localNow)
    {
        var currentMinute = localNow.Hour * 60 + localNow.Minute;
        return hours.Any(day => day.IsOpen && day.Shifts.Any(shift => IsShiftOpen(day.DayOfWeek, shift, localNow.DayOfWeek, currentMinute)));
    }

    private static bool IsShiftOpen(DayOfWeek shiftDay, StoreBusinessHourShift shift, DayOfWeek currentDay, int currentMinute)
    {
        var start = ToMinute(shift.StartTime);
        var end = ToMinute(shift.EndTime);

        if (start == end)
        {
            return shiftDay == currentDay;
        }

        if (end > start)
        {
            return shiftDay == currentDay && currentMinute >= start && currentMinute < end;
        }

        var nextDay = (DayOfWeek)(((int)shiftDay + 1) % 7);
        return (shiftDay == currentDay && currentMinute >= start)
            || (nextDay == currentDay && currentMinute < end);
    }

    private static DateTimeOffset? FindNextOpening(IReadOnlyCollection<StoreBusinessHour> hours, DateTimeOffset localNow)
    {
        var localDate = DateOnly.FromDateTime(localNow.DateTime);
        DateTimeOffset? best = null;

        for (var offset = 0; offset <= 7; offset++)
        {
            var date = localDate.AddDays(offset);
            var dayOfWeek = date.DayOfWeek;

            foreach (var shift in hours.Where(hour => hour.IsOpen && hour.DayOfWeek == dayOfWeek).SelectMany(hour => hour.Shifts))
            {
                var candidate = new DateTimeOffset(date.ToDateTime(shift.StartTime), localNow.Offset);
                if (candidate <= localNow)
                {
                    continue;
                }

                if (best is null || candidate < best)
                {
                    best = candidate;
                }
            }
        }

        return best;
    }

    private static DateTimeOffset? FindNextClosing(IReadOnlyCollection<StoreBusinessHour> hours, DateTimeOffset localNow)
    {
        var localDate = DateOnly.FromDateTime(localNow.DateTime);
        DateTimeOffset? best = null;

        for (var offset = -1; offset <= 7; offset++)
        {
            var startDate = localDate.AddDays(offset);
            var shiftDay = startDate.DayOfWeek;

            foreach (var shift in hours.Where(hour => hour.IsOpen && hour.DayOfWeek == shiftDay).SelectMany(hour => hour.Shifts))
            {
                var start = new DateTimeOffset(startDate.ToDateTime(shift.StartTime), localNow.Offset);
                var endDate = shift.EndTime <= shift.StartTime ? startDate.AddDays(1) : startDate;
                var end = new DateTimeOffset(endDate.ToDateTime(shift.EndTime), localNow.Offset);

                if (shift.StartTime == shift.EndTime)
                {
                    end = start.AddDays(1);
                }

                if (start <= localNow && localNow < end && (best is null || end < best))
                {
                    best = end;
                }
            }
        }

        return best;
    }

    private static DateTimeOffset? ToUtc(DateTimeOffset? value)
    {
        return value?.ToUniversalTime();
    }

    private static int ToMinute(TimeOnly time)
    {
        return time.Hour * 60 + time.Minute;
    }

    private static string FormatDay(DayOfWeek day)
    {
        return day switch
        {
            DayOfWeek.Sunday => "Domingo",
            DayOfWeek.Monday => "Segunda",
            DayOfWeek.Tuesday => "Terça",
            DayOfWeek.Wednesday => "Quarta",
            DayOfWeek.Thursday => "Quinta",
            DayOfWeek.Friday => "Sexta",
            DayOfWeek.Saturday => "Sábado",
            _ => day.ToString()
        };
    }

    private static TimeZoneInfo ResolveSaoPauloTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");
        }
    }
}

public sealed record StoreOpeningHoursStatus(
    bool IsOpenNow,
    DateTimeOffset? NextOpeningAt,
    DateTimeOffset? NextStatusChangeAtUtc,
    string? ClosedMessage);
