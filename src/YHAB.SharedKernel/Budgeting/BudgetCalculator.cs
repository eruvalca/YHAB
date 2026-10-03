namespace YHAB.SharedKernel.Budgeting;

/// <summary>Projects the ledger into monthly envelopes, cash overspending, and credit payment reserves.</summary>
public static class BudgetCalculator
{
    public static BudgetMonth Calculate(PlanSnapshot plan, DateOnly month, DateOnly today)
        => CalculateCore(plan, month, today, includeTargets: true);

    internal static decimal CarryInto(PlanSnapshot plan, Guid categoryId, DateOnly month)
        => Math.Max(0, CalculateCore(plan, month.AddMonths(-1), plan.Today, includeTargets: false)
            .Categories.Single(item => item.Category.Id == categoryId).Available);

    private static BudgetMonth CalculateCore(PlanSnapshot plan, DateOnly month, DateOnly today, bool includeTargets)
    {
        ArgumentNullException.ThrowIfNull(plan);
        month = BudgetFacts.Month(month);
        var start = EarliestMonth(plan, month);
        var carry = plan.Categories.ToDictionary(item => item.Id, _ => 0m);
        var balances = plan.Accounts.ToDictionary(item => item.Id, _ => 0m);
        var accounts = plan.Accounts.ToDictionary(item => item.Id);
        var payments = plan.Categories.Where(item => item.CreditAccountId.HasValue)
            .ToDictionary(item => item.CreditAccountId!.Value, item => item.Id);
        var entries = plan.Transactions.Where(item => item.Repeat == RepeatFrequency.None)
            .OrderBy(item => item.Date).ThenBy(item => item.Id).ToLookup(item => BudgetFacts.Month(item.Date));
        BudgetMonth? result = null;
        for (var current = start; current <= month; current = current.AddMonths(1))
        {
            var creditMoves = new List<CreditMove>();
            var rows = plan.Categories.ToDictionary(item => item.Id, item => new WorkingCategory(item, carry[item.Id]));
            foreach (var allocation in plan.Allocations.Where(item => item.Month == current))
            {
                if (rows.TryGetValue(allocation.CategoryId, out var row))
                {
                    row.Assigned = allocation.Amount;
                    row.Snoozed = allocation.Snoozed;
                }
            }

            foreach (var account in plan.Accounts.Where(item => BudgetFacts.Month(item.OpenedOn) == current))
            {
                balances[account.Id] += account.OpeningBalance;
            }

            foreach (var entry in entries[current])
            {
                ApplyEntry(entry, accounts, balances, rows, payments, creditMoves);
            }

            FundCredit(rows, payments);
            MoveCreditReserves(rows, creditMoves);
            var output = rows.Values.Select(row => ToCategoryMonth(plan, row, current, today, includeTargets)).ToArray();
            var liquid = plan.Accounts.Where(account => BudgetFacts.IsCash(account.Kind)).Sum(account => balances[account.Id])
                + plan.Accounts.Where(account => BudgetFacts.IsCredit(account.Kind)).Sum(account => Math.Max(0, balances[account.Id]));
            var future = plan.Allocations.Where(item => item.Month > current).Sum(item => item.Amount);
            var creditOverspending = output.Sum(item => item.CreditOverspending);
            var available = output.Sum(item => item.Available);
            result = new(current, liquid - available - creditOverspending - future, output.Sum(item => item.Assigned),
                output.Sum(item => item.Activity), available, output.Sum(item => item.CashOverspending), creditOverspending, future, output);
            carry = output.ToDictionary(item => item.Category.Id, item => Math.Max(0, item.Available));
        }

        return result!;
    }

    private static DateOnly EarliestMonth(PlanSnapshot plan, DateOnly month)
    {
        var dates = plan.Accounts.Select(item => item.OpenedOn)
            .Concat(plan.Transactions.Select(item => item.Date))
            .Concat(plan.Allocations.Select(item => item.Month)).Append(plan.CreatedOn).Append(month);
        return BudgetFacts.Month(dates.Min());
    }

    private static void ApplyEntry(TransactionData entry, Dictionary<Guid, AccountData> accounts,
        Dictionary<Guid, decimal> balances, Dictionary<Guid, WorkingCategory> rows, Dictionary<Guid, Guid> payments, List<CreditMove> creditMoves)
    {
        var source = accounts[entry.AccountId];
        var before = balances[source.Id];
        balances[source.Id] += entry.Amount;
        AccountData? destination = null;
        var destinationBefore = 0m;
        if (entry.TransferAccountId is { } transferId)
        {
            destination = accounts[transferId];
            destinationBefore = balances[transferId];
            ApplyPayment(entry, source, destination, balances, rows, payments, creditMoves);
            balances[transferId] -= entry.Amount;
        }

        // Transfers between budget accounts move money, not spending-category activity.
        if (destination is not null && BudgetFacts.IsBudget(source.Kind) && BudgetFacts.IsBudget(destination.Kind))
        {
            return;
        }

        var budgetAccount = BudgetFacts.IsBudget(source.Kind) ? source : destination;
        if (budgetAccount is null || !BudgetFacts.IsBudget(budgetAccount.Kind))
        {
            return;
        }

        var sign = budgetAccount.Id == source.Id ? 1 : -1;
        var budgetBefore = budgetAccount.Id == source.Id ? before : destinationBefore;
        var budgetAmount = entry.Amount * sign;
        var debtChange = Math.Min(0, budgetBefore + budgetAmount) - Math.Min(0, budgetBefore);
        var remainingCredit = debtChange;
        var remainingAmount = budgetAmount;
        foreach (var split in entry.Splits)
        {
            if (split.CategoryId is not { } categoryId || !rows.TryGetValue(categoryId, out var row))
            {
                continue;
            }

            var amount = split.Amount * sign;
            row.Activity += amount;
            if (BudgetFacts.IsCredit(budgetAccount.Kind))
            {
                var fraction = budgetAmount == 0 ? 0 : debtChange / budgetAmount;
                var creditAmount = amount == remainingAmount ? remainingCredit : decimal.Round(amount * fraction, 2, MidpointRounding.AwayFromZero);
                remainingCredit -= creditAmount;
                remainingAmount -= amount;
                row.CreditActivity[budgetAccount.Id] = row.CreditActivity.GetValueOrDefault(budgetAccount.Id) + creditAmount;
                row.CashActivity += amount - creditAmount;
            }
            else
            {
                row.CashActivity += amount;
            }
        }
    }

    private static void ApplyPayment(TransactionData entry, AccountData source, AccountData destination,
        Dictionary<Guid, decimal> balances, Dictionary<Guid, WorkingCategory> rows, Dictionary<Guid, Guid> payments, List<CreditMove> creditMoves)
    {
        var receiving = entry.Amount < 0 ? destination : source;
        var sending = entry.Amount < 0 ? source : destination;
        var receivingBefore = receiving.Id == source.Id ? balances[source.Id] - entry.Amount : balances[destination.Id];
        var paid = Math.Min(Math.Abs(entry.Amount), Math.Max(0, -receivingBefore));
        if (!BudgetFacts.IsCredit(receiving.Kind) || !payments.TryGetValue(receiving.Id, out var categoryId))
        {
            return;
        }

        if (BudgetFacts.IsCash(sending.Kind))
        {
            rows[categoryId].Activity -= paid;
            rows[categoryId].CashActivity -= paid;
        }
        else if (BudgetFacts.IsCredit(sending.Kind) && payments.TryGetValue(sending.Id, out var sendingCategory))
        {
            creditMoves.Add(new(categoryId, sendingCategory, paid));
        }
    }

    private static void MoveCreditReserves(Dictionary<Guid, WorkingCategory> rows, List<CreditMove> moves)
    {
        foreach (var move in moves)
        {
            var source = rows[move.ReceivingCategory];
            var reserved = Math.Min(move.Paid, Math.Max(0, source.Carried + source.Assigned + source.Activity));
            source.Activity -= reserved;
            rows[move.SendingCategory].Activity += reserved;
        }
    }

    private sealed record CreditMove(Guid ReceivingCategory, Guid SendingCategory, decimal Paid);

    private static void FundCredit(Dictionary<Guid, WorkingCategory> rows, Dictionary<Guid, Guid> payments)
    {
        foreach (var row in rows.Values.Where(item => item.Category.CreditAccountId is null))
        {
            var funds = Math.Max(0, row.Carried + row.Assigned + row.CashActivity);
            foreach (var (accountId, activity) in row.CreditActivity)
            {
                if (!payments.TryGetValue(accountId, out var paymentId))
                {
                    continue;
                }

                var reserved = activity >= 0 ? -activity : Math.Min(-activity, funds);
                rows[paymentId].Activity += reserved;
                funds -= reserved;
            }
        }
    }

    private static CategoryMonth ToCategoryMonth(PlanSnapshot plan, WorkingCategory row, DateOnly month, DateOnly today, bool includeTargets)
    {
        var available = row.Carried + row.Assigned + row.Activity;
        var overspending = Math.Max(0, -available);
        var cash = row.Category.CreditAccountId.HasValue ? overspending
            : Math.Min(overspending, Math.Max(0, -(row.Carried + row.Assigned + row.CashActivity)));
        var needed = row.Snoozed || !includeTargets ? 0 : TargetCalculator.Needed(plan, row.Category, month, today, row.Assigned, available, row.Carried);
        var targetTotal = !includeTargets ? 0 : MonthlyTargetTotal(plan, row, month, today, available);
        return new(row.Category, row.Assigned, row.Activity, available, cash, overspending - cash, needed,
            targetTotal, row.Snoozed);
    }

    private static decimal MonthlyTargetTotal(PlanSnapshot plan, WorkingCategory row, DateOnly month, DateOnly today, decimal available)
        => row.Category.Target?.Cadence is TargetCadence.Monthly or TargetCadence.Weekly
            ? TargetCalculator.Needed(plan, row.Category, month, today, 0, 0, 0)
            : TargetCalculator.Needed(plan, row.Category, month, today, 0, available - row.Assigned, row.Carried);

    private sealed class WorkingCategory(CategoryData category, decimal carried)
    {
        public CategoryData Category { get; } = category;
        public decimal Carried { get; } = carried;
        public decimal Assigned { get; set; }
        public decimal Activity { get; set; }
        public decimal CashActivity { get; set; }
        public bool Snoozed { get; set; }
        public Dictionary<Guid, decimal> CreditActivity { get; } = [];
    }
}
