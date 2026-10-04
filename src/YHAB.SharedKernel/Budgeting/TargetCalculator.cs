namespace YHAB.SharedKernel.Budgeting;

public static class TargetCalculator
{
    public static decimal Needed(PlanSnapshot plan, CategoryData category, DateOnly month, DateOnly today,
        decimal assigned, decimal available, decimal carried)
        => Needed(plan, category, month, today, assigned, available, carried, (id, period) => BudgetCalculator.CarryInto(plan, id, period));

    internal static decimal Needed(PlanSnapshot plan, CategoryData category, DateOnly month, DateOnly today,
        decimal assigned, decimal available, decimal carried, Func<Guid, DateOnly, decimal> carry, decimal? priorAssigned = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(category);
        var target = category.Target;
        if (target is null || month < target.StartMonth)
        {
            return 0;
        }

        if (target.Cadence is TargetCadence.Monthly or TargetCadence.Weekly)
        {
            var amount = target.Amount;
            if (target.Cadence == TargetCadence.Weekly)
            {
                var first = month.AddDays(((int)target.Weekday - (int)month.DayOfWeek + 7) % 7);
                amount *= 1 + ((DateTime.DaysInMonth(month.Year, month.Month) - first.Day) / 7);
            }

            var contribution = target.Kind == TargetKind.Balance ? available : assigned;
            if (target.Kind == TargetKind.Refill && month <= BudgetFacts.Month(today))
            {
                contribution += Math.Max(0, carried);
            }

            return Math.Max(0, amount - contribution);
        }

        return DatedTarget(plan, category, month, available, assigned, carry, priorAssigned);
    }

    /// <summary>Returns the active dated cycle, or null for an inactive or undated target.</summary>
    public static TargetPeriod? PeriodFor(TargetData? target, DateOnly month)
    {
        if (target?.DueDate is not { } due || month < target.StartMonth || target.Cadence is TargetCadence.Monthly or TargetCadence.Weekly)
        {
            return null;
        }
        var dueMonth = BudgetFacts.Month(due);
        var start = target.StartMonth;
        var repeat = target.Cadence == TargetCadence.Yearly ? 12 : target.RepeatEveryMonths;
        if (repeat == 0 && month > dueMonth)
        {
            return null;
        }

        while (month > dueMonth)
        {
            start = dueMonth.AddMonths(1);
            dueMonth = dueMonth.AddMonths(repeat);
        }
        return new(start, dueMonth);
    }

    private static decimal DatedTarget(PlanSnapshot plan, CategoryData category, DateOnly month, decimal available, decimal assigned,
        Func<Guid, DateOnly, decimal> carry, decimal? priorAssigned)
    {
        var target = category.Target!;
        if (target.DueDate is null)
        {
            return Math.Max(0, target.Amount - available);
        }
        if (PeriodFor(target, month) is not { } period) { return 0; }
        var monthsLeft = ((period.Due.Year - month.Year) * 12) + period.Due.Month - month.Month + 1;
        priorAssigned ??= plan.Allocations.Where(item => item.CategoryId == category.Id && item.Month >= period.Start && item.Month < month)
            .Sum(item => item.Amount);
        var previousProgress = target.Kind switch
        {
            TargetKind.SetAside => priorAssigned.Value,
            TargetKind.Refill => priorAssigned.Value + carry(category.Id, period.Start),
            _ => available - assigned,
        };
        var perMonth = decimal.Ceiling(Math.Max(0, target.Amount - previousProgress) / monthsLeft * 100) / 100;
        return Math.Max(0, perMonth - assigned);
    }
}
