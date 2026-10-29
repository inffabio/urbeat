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
                // Closed days must not fail validation because of shifts that are hidden
                // by the editor and are never persisted for that day.
                item.When(x => x.IsOpen, () =>
                {
                    item.RuleFor(x => x.Shifts)
                        .NotEmpty()
                        .WithMessage("Open days must have at least one shift.");

                    item.RuleFor(x => x.Shifts.Count)
                        .LessThanOrEqualTo(MaxShiftsPerDay)
                        .WithMessage($"Maximum {MaxShiftsPerDay} shifts per day.");

                    item.RuleForEach(x => x.Shifts)
                        .ChildRules(shift =>
                        {
                            shift.RuleFor(x => x.StartTime)
                                .NotNull()
                                .WithMessage("Start time is required.");

                            shift.RuleFor(x => x.EndTime)
                                .NotNull()
                                .WithMessage("End time is required.");
                        });

                    item.RuleFor(x => x.Shifts)
                        .Must(shifts => !HasOverlappingShifts(shifts))
                        .WithMessage("Shifts cannot overlap within the same day.");

                    item.RuleFor(x => x.Shifts)
                        .Must(shifts => !HasEqualStartAndEndTimes(shifts))
                        .WithMessage("Start and end times cannot be equal.");
                });
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
                if (!shift.StartTime.HasValue || !shift.EndTime.HasValue) continue;

                var start = shift.StartTime.Value.ToTimeSpan().TotalMinutes;
                var end = shift.EndTime.Value.ToTimeSpan().TotalMinutes;
                if (end >= start) continue;

                foreach (var nextShift in nextDay.Shifts)
                {
                    if (!nextShift.StartTime.HasValue) continue;

                    if (nextShift.StartTime.Value.ToTimeSpan().TotalMinutes < end)
                        return false;
                }
            }
        }

        return true;
    }

    private static bool HasOverlappingShifts(IEnumerable<StoreBusinessHourShiftDto> shifts)
    {
        var list = shifts.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            for (int j = i + 1; j < list.Count; j++)
            {
                if (ShiftsOverlap(list[i], list[j]))
                    return true;
            }
        }

        return false;
    }

    private static bool ShiftsOverlap(StoreBusinessHourShiftDto a, StoreBusinessHourShiftDto b)
    {
        if (!a.StartTime.HasValue || !a.EndTime.HasValue
            || !b.StartTime.HasValue || !b.EndTime.HasValue)
        {
            return false;
        }

        var aStart = a.StartTime.Value.ToTimeSpan().TotalMinutes;
        var aEnd = a.EndTime.Value.ToTimeSpan().TotalMinutes;
        if (aEnd <= aStart) aEnd += 1440;

        var bStart = b.StartTime.Value.ToTimeSpan().TotalMinutes;
        var bEnd = b.EndTime.Value.ToTimeSpan().TotalMinutes;
        if (bEnd <= bStart) bEnd += 1440;

        return aStart < bEnd && bStart < aEnd
            || (aStart < bEnd + 1440 && bStart + 1440 < aEnd)
            || (aStart + 1440 < bEnd && bStart < aEnd + 1440);
    }

    private static bool HasEqualStartAndEndTimes(IEnumerable<StoreBusinessHourShiftDto> shifts)
        => shifts.Any(shift =>
            shift.StartTime.HasValue
            && shift.EndTime.HasValue
            && shift.StartTime.Value == shift.EndTime.Value);
}
