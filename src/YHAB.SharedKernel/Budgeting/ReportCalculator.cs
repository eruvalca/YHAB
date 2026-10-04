namespace YHAB.SharedKernel.Budgeting;

/// <summary>Cash-flow reports exclude opening balances and transfers between budget accounts.</summary>
public static class ReportCalculator
{
    public static IReadOnlyList<MonthReport> Months(PlanSnapshot plan, DateOnly from, DateOnly through)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var result = new List<MonthReport>();
        for (var month = BudgetFacts.Month(from); month <= through; month = month.AddMonths(1))
        {
            var end = month.AddMonths(1).AddDays(-1);
            var startDate = month < from ? from : month;
            var endDate = end > through ? through : end;
            var income = 0m;
            var expense = 0m;
            foreach (var entry in plan.Transactions.Where(item => item.Repeat == RepeatFrequency.None && item.Date >= startDate && item.Date <= endDate))
            {
                var direction = BudgetDirection(plan, entry);
                income += entry.Splits.Where(item => item.CategoryId is null).Sum(item => item.Amount) * direction;
                expense -= entry.Splits.Where(item => item.CategoryId.HasValue).Sum(item => item.Amount) * direction;
            }
            var balances = plan.Accounts.Select(account => BudgetFacts.Balance(plan, account, endDate).Working).ToArray();
            result.Add(new(month, income, expense, balances.Where(value => value > 0).Sum(), -balances.Where(value => value < 0).Sum()));
        }
        return result;
    }

    public static IReadOnlyList<CategorySpending> Spending(PlanSnapshot plan, DateOnly from, DateOnly through)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.Transactions.Where(item => item.Repeat == RepeatFrequency.None && item.Date >= from && item.Date <= through)
            .SelectMany(entry => entry.Splits.Where(split => split.CategoryId.HasValue)
                .Select(split => new { Id = split.CategoryId!.Value, Amount = -split.Amount * BudgetDirection(plan, entry) }))
            .GroupBy(item => item.Id)
            .Select(group => new CategorySpending(group.Key, plan.Categories.Single(item => item.Id == group.Key).Name, group.Sum(item => item.Amount)))
            .Where(item => item.Amount != 0).OrderByDescending(item => item.Amount).ThenBy(item => item.CategoryId).ToArray();
    }

    /// <summary>Builds the same reports from bounded aggregates without retaining transactions. Totals must cover the supplied inclusive date range.</summary>
    public static ReportView FromTotals(PlanSnapshot catalog, DateOnly from, DateOnly through, ReportTotals totals)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(totals);
        var changes = catalog.Accounts.ToDictionary(account => account.Id, account => totals.PriorAccountChanges.GetValueOrDefault(account.Id));
        var movements = totals.AccountMonths.ToLookup(item => item.Month);
        var flows = totals.CategoryMonths.ToLookup(item => item.Month);
        var months = new List<MonthReport>();
        for (var month = BudgetFacts.Month(from); month <= through; month = month.AddMonths(1))
        {
            foreach (var movement in movements[month]) { changes[movement.AccountId] += movement.Amount; }
            var end = month.AddMonths(1).AddDays(-1);
            var throughDate = end > through ? through : end;
            var balances = catalog.Accounts.Select(account => changes[account.Id]
                + (account.OpenedOn <= throughDate ? account.OpeningBalance : 0)).ToArray();
            months.Add(new(month, flows[month].Where(item => item.CategoryId is null).Sum(item => item.Amount),
                -flows[month].Where(item => item.CategoryId.HasValue).Sum(item => item.Amount),
                balances.Where(amount => amount > 0).Sum(), -balances.Where(amount => amount < 0).Sum()));
        }
        var names = catalog.Categories.ToDictionary(item => item.Id, item => item.Name);
        var spending = totals.CategoryMonths.Where(item => item.CategoryId.HasValue)
            .GroupBy(item => item.CategoryId!.Value)
            .Select(group => new CategorySpending(group.Key, names[group.Key], -group.Sum(item => item.Amount)))
            .Where(item => item.Amount != 0).OrderByDescending(item => item.Amount).ThenBy(item => item.CategoryId).ToArray();
        return new(catalog.Version, months, spending);
    }

    private static int BudgetDirection(PlanSnapshot plan, TransactionData entry)
    {
        var source = plan.Accounts.Single(item => item.Id == entry.AccountId);
        var destination = plan.Accounts.SingleOrDefault(item => item.Id == entry.TransferAccountId);
        if (BudgetFacts.IsBudget(source.Kind))
        {
            return destination is not null && BudgetFacts.IsBudget(destination.Kind) ? 0 : 1;
        }
        return destination is not null && BudgetFacts.IsBudget(destination.Kind) ? -1 : 0;
    }
}
