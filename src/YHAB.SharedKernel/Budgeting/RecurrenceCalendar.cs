namespace YHAB.SharedKernel.Budgeting;

public static class RecurrenceCalendar
{
    public static DateOnly DateAt(DateOnly anchor, RepeatFrequency frequency, int occurrence)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(occurrence);
        return frequency switch
        {
            RepeatFrequency.Daily => anchor.AddDays(occurrence),
            RepeatFrequency.Weekly => anchor.AddDays(checked(7 * occurrence)),
            RepeatFrequency.EveryTwoWeeks => anchor.AddDays(checked(14 * occurrence)),
            RepeatFrequency.TwiceMonthly => TwiceMonthly(anchor, occurrence),
            RepeatFrequency.Monthly => anchor.AddMonths(occurrence),
            RepeatFrequency.EveryTwoMonths => anchor.AddMonths(checked(2 * occurrence)),
            RepeatFrequency.Quarterly => anchor.AddMonths(checked(3 * occurrence)),
            RepeatFrequency.EverySixMonths => anchor.AddMonths(checked(6 * occurrence)),
            RepeatFrequency.Yearly => anchor.AddYears(occurrence),
            _ => throw new ArgumentOutOfRangeException(nameof(frequency)),
        };
    }

    private static DateOnly TwiceMonthly(DateOnly anchor, int occurrence)
    {
        // Preserve a pair of calendar days (e.g. 1/16 or 15/30), clamping shorter months without drift.
        var firstDay = anchor.Day > 15 ? anchor.Day - 15 : anchor.Day;
        var slot = occurrence + (anchor.Day > 15 ? 1 : 0);
        var month = BudgetFacts.Month(anchor).AddMonths(slot / 2);
        var day = firstDay + (slot % 2 * 15);
        return new(month.Year, month.Month, Math.Min(day, DateTime.DaysInMonth(month.Year, month.Month)));
    }
}
