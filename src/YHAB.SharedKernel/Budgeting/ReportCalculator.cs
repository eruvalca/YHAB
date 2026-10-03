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
            .Where(item => item.Amount != 0).OrderByDescending(item => item.Amount).ToArray();
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

