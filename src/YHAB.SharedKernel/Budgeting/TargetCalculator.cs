namespace YHAB.SharedKernel.Budgeting;

public static class TargetCalculator
{
    public static decimal Needed(PlanSnapshot plan, CategoryData category, DateOnly month, DateOnly today,
        decimal assigned, decimal available, decimal carried)
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

        return DatedTarget(plan, category, month, available, assigned);
    }

    private static decimal DatedTarget(PlanSnapshot plan, CategoryData category, DateOnly month, decimal available, decimal assigned)
    {
        var target = category.Target!;
        if (target.DueDate is null)
        {
            return Math.Max(0, target.Amount - available);
        }

        var dueMonth = BudgetFacts.Month(target.DueDate.Value);
        var start = target.StartMonth;
        var repeat = target.Cadence == TargetCadence.Yearly ? 12 : target.RepeatEveryMonths;
        if (repeat == 0 && month > dueMonth)
        {
            return 0;
        }

        while (month > dueMonth)
        {
            start = dueMonth.AddMonths(1);
            dueMonth = dueMonth.AddMonths(repeat);
        }

        var monthsLeft = ((dueMonth.Year - month.Year) * 12) + dueMonth.Month - month.Month + 1;
        var priorAssigned = plan.Allocations.Where(item => item.CategoryId == category.Id && item.Month >= start && item.Month < month)
            .Sum(item => item.Amount);
        var previousProgress = target.Kind switch
        {
            TargetKind.SetAside => priorAssigned,
            TargetKind.Refill => priorAssigned + BudgetCalculator.CarryInto(plan, category.Id, start),
            _ => available - assigned,
        };
        var perMonth = decimal.Ceiling(Math.Max(0, target.Amount - previousProgress) / monthsLeft * 100) / 100;
        return Math.Max(0, perMonth - assigned);
    }
}
