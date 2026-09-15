using FluentValidation;
using Urbeat.Application.DTOs;

namespace Urbeat.Application.Validators;

public sealed class UpsertStoreBusinessHoursRequestDtoValidator : AbstractValidator<UpsertStoreBusinessHoursRequestDto>
{
    private const int MaxShiftsPerDay = 5;

    public UpsertStoreBusinessHoursRequestDtoValidator()
    {
        RuleFor(x => x.Items)
            .NotEmpty();

        RuleFor(x => x.Items)
            .Must(items => items.Select(i => i.DayOfWeek).Distinct().Count() == items.Count)
            .WithMessage("Each day of week can be configured only once.");

        RuleFor(x => x.Items)
            .Must(HaveNoCrossDayOvernightConflicts)
            .WithMessage("Overnight shifts cannot overlap the next day's shifts.");

        RuleForEach(x => x.Items)
            .ChildRules(item =>
            {
                item.RuleFor(x => x.Shifts)
                    .NotEmpty()
                    .When(x => x.IsOpen)
                    .WithMessage("Open days must have at least one shift.");

                item.RuleFor(x => x.Shifts.Count)
                    .LessThanOrEqualTo(MaxShiftsPerDay)
                    .WithMessage($"Maximum {MaxShiftsPerDay} shifts per day.");

                item.RuleForEach(x => x.Shifts)
                    .ChildRules(shift =>
                    {
                        shift.RuleFor(x => x.StartTime)
                            .NotEmpty()
                            .WithMessage("Start time is required.");

                        shift.RuleFor(x => x.EndTime)
                            .NotEmpty()
                            .WithMessage("End time is required.");
                    });

                item.RuleFor(x => x.Shifts)
                    .Must(shifts =>
                    {
                        var list = shifts.ToList();
                        for (int i = 0; i < list.Count; i++)
                        {
                            for (int j = i + 1; j < list.Count; j++)
                            {
                                if (ShiftsOverlap(list[i], list[j]))
                                    return false;
                            }
                        }
                        return true;
                    })
                    .WithMessage("Shifts cannot overlap within the same day.");

                item.RuleFor(x => x.Shifts)
                    .Must(shifts => shifts.All(s => s.StartTime != s.EndTime))
                    .WithMessage("Start and end times cannot be equal.");
            });
    }

    private static bool HaveNoCrossDayOvernightConflicts(
        IReadOnlyCollection<StoreBusinessHourItemDto> items)
    {
        var daysByDayOfWeek = items.ToLookup(item => item.DayOfWeek);

        // Duplicated days are reported by the uniqueness rule. Skip the
        // cross-day check so a lookup never throws and does not add a
        // competing error for an already invalid schedule.
        if (daysByDayOfWeek.Any(group => group.Count() > 1))
            return true;

        foreach (var item in items)
        {
            if (!item.IsOpen) continue;

            var nextDayOfWeek = (DayOfWeek)(((int)item.DayOfWeek + 1) % 7);
            var nextDay = daysByDayOfWeek[nextDayOfWeek].FirstOrDefault();
            if (nextDay is null || !nextDay.IsOpen) continue;

            foreach (var shift in item.Shifts)
            {
                var start = shift.StartTime.ToTimeSpan().TotalMinutes;
                var end = shift.EndTime.ToTimeSpan().TotalMinutes;
                if (end >= start) continue;

                foreach (var nextShift in nextDay.Shifts)
                {
                    if (nextShift.StartTime.ToTimeSpan().TotalMinutes < end)
                        return false;
                }
            }
        }

        return true;
    }

    private static bool ShiftsOverlap(StoreBusinessHourShiftDto a, StoreBusinessHourShiftDto b)
    {
        var aStart = a.StartTime.ToTimeSpan().TotalMinutes;
        var aEnd = a.EndTime.ToTimeSpan().TotalMinutes;
        if (aEnd <= aStart) aEnd += 1440;

        var bStart = b.StartTime.ToTimeSpan().TotalMinutes;
        var bEnd = b.EndTime.ToTimeSpan().TotalMinutes;
        if (bEnd <= bStart) bEnd += 1440;

        return aStart < bEnd && bStart < aEnd
            || (aStart < bEnd + 1440 && bStart + 1440 < aEnd)
            || (aStart + 1440 < bEnd && bStart < aEnd + 1440);
    }
}
