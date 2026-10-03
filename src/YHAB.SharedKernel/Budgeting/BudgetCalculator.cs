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
            var creditActivity = new List<CreditActivity>();
            var creditPayments = new Dictionary<Guid, CreditPayment>();
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
                ApplyEntry(entry, accounts, balances, rows, payments, creditActivity, creditPayments);
            }

            var funded = FundCredit(rows, creditActivity);
            ApplyCreditReserves(entries[current], rows, funded, creditPayments);
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
        Dictionary<Guid, decimal> balances, Dictionary<Guid, WorkingCategory> rows, Dictionary<Guid, Guid> payments,
        List<CreditActivity> creditActivity, Dictionary<Guid, CreditPayment> creditPayments)
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
            RecordPayment(entry, source, destination, before, destinationBefore, payments, creditPayments);
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
                row.CreditRefunds += Math.Max(0, creditAmount);
                if (payments.TryGetValue(budgetAccount.Id, out var paymentId))
                {
                    creditActivity.Add(new(entry.Id, categoryId, paymentId, creditAmount));
                }
                row.CashActivity += amount - creditAmount;
            }
            else
            {
                row.CashActivity += amount;
            }
        }
    }

    private static void RecordPayment(TransactionData entry, AccountData source, AccountData destination,
        decimal sourceBefore, decimal destinationBefore, Dictionary<Guid, Guid> payments, Dictionary<Guid, CreditPayment> creditPayments)
    {
        var receiving = entry.Amount < 0 ? destination : source;
        var sending = entry.Amount < 0 ? source : destination;
        var receivingBefore = receiving.Id == source.Id ? sourceBefore : destinationBefore;
        var paid = Math.Min(Math.Abs(entry.Amount), Math.Max(0, -receivingBefore));
        if (!BudgetFacts.IsCredit(receiving.Kind) || !payments.TryGetValue(receiving.Id, out var categoryId))
        {
            return;
        }

        if (BudgetFacts.IsCash(sending.Kind))
        {
            creditPayments.Add(entry.Id, new(categoryId, null, paid, 0));
        }
        else if (BudgetFacts.IsCredit(sending.Kind) && payments.TryGetValue(sending.Id, out var sendingCategory))
        {
            var sendingBefore = sending.Id == source.Id ? sourceBefore : destinationBefore;
            var borrowed = Math.Max(0, paid - Math.Max(0, sendingBefore));
            creditPayments.Add(entry.Id, new(categoryId, sendingCategory, paid, borrowed));
        }
    }

    private static void ApplyCreditReserves(IEnumerable<TransactionData> entries, Dictionary<Guid, WorkingCategory> rows,
        Dictionary<Guid, PaymentFunding> funded, Dictionary<Guid, CreditPayment> payments)
    {
        // Category funding accounts for the whole month's cash spending and refunds.
        // Apply that funding at each purchase, so a transfer cannot consume later purchases' reserves.
        foreach (var entry in entries)
        {
            if (funded.TryGetValue(entry.Id, out var funding))
            {
                rows[funding.PaymentCategoryId].Activity += funding.Amount;
            }

            if (!payments.TryGetValue(entry.Id, out var payment))
            {
                continue;
            }

            var receiving = rows[payment.ReceivingCategory];
            var cashPaid = payment.Paid - payment.Borrowed;
            receiving.Activity -= cashPaid;
            receiving.CashActivity -= cashPaid;
            if (payment.SendingCategory is { } sendingCategory)
            {
                var reserved = Math.Min(payment.Borrowed, Math.Max(0, receiving.Carried + receiving.Assigned + receiving.Activity));
                receiving.Activity -= reserved;
                rows[sendingCategory].Activity += reserved;
            }
        }
    }

    private sealed record CreditActivity(Guid TransactionId, Guid CategoryId, Guid PaymentCategoryId, decimal Amount);
    private sealed record PaymentFunding(Guid PaymentCategoryId, decimal Amount);
    private sealed record CreditPayment(Guid ReceivingCategory, Guid? SendingCategory, decimal Paid, decimal Borrowed);

    private static Dictionary<Guid, PaymentFunding> FundCredit(Dictionary<Guid, WorkingCategory> rows, List<CreditActivity> activity)
    {
        var remainingActivity = OffsetRefundedPurchases(rows, activity);
        // Cash spending has priority when allocating the remaining category money.
        var funds = rows.ToDictionary(item => item.Key,
            item => Math.Max(0, item.Value.Carried + item.Value.Assigned + item.Value.CashActivity + item.Value.CreditRefunds));
        var funded = new Dictionary<Guid, PaymentFunding>();
        foreach (var item in remainingActivity)
        {
            var reserved = -item.Amount;
            if (item.Amount < 0)
            {
                reserved = Math.Min(reserved, funds[item.CategoryId]);
                funds[item.CategoryId] -= reserved;
            }

            funded[item.TransactionId] = new(item.PaymentCategoryId, reserved + (funded.GetValueOrDefault(item.TransactionId)?.Amount ?? 0));
        }
        return funded;
    }

    private static CreditActivity[] OffsetRefundedPurchases(Dictionary<Guid, WorkingCategory> rows, List<CreditActivity> activity)
    {
        var remaining = activity.ToArray();
        var purchases = new Dictionary<(Guid CategoryId, Guid PaymentCategoryId), Stack<int>>();
        for (var index = 0; index < remaining.Length; index++)
        {
            var item = remaining[index];
            var key = (item.CategoryId, item.PaymentCategoryId);
            if (!purchases.TryGetValue(key, out var previous))
            {
                previous = new Stack<int>();
                purchases.Add(key, previous);
            }

            if (item.Amount < 0)
            {
                previous.Push(index);
                continue;
            }

            // Cancel the most recent earlier purchases on this card/category. These
            // offsets reduce spending; they never become reserves an earlier transfer can use.
            var refund = item.Amount;
            while (refund > 0 && previous.TryPeek(out var purchaseIndex))
            {
                var purchase = remaining[purchaseIndex];
                var canceled = Math.Min(refund, -purchase.Amount);
                remaining[purchaseIndex] = purchase with { Amount = purchase.Amount + canceled };
                refund -= canceled;
                if (remaining[purchaseIndex].Amount == 0)
                {
                    previous.Pop();
                }
            }

            rows[item.CategoryId].CreditRefunds -= item.Amount - refund;
            remaining[index] = item with { Amount = refund };
        }

        return remaining;
    }

    private static CategoryMonth ToCategoryMonth(PlanSnapshot plan, WorkingCategory row, DateOnly month, DateOnly today, bool includeTargets)
    {
        var available = row.Carried + row.Assigned + row.Activity;
        var overspending = Math.Max(0, -available);
        var cash = row.Category.CreditAccountId.HasValue ? overspending
            : Math.Min(overspending, Math.Max(0, -(row.Carried + row.Assigned + row.CashActivity + row.CreditRefunds)));
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
        public decimal CreditRefunds { get; set; }
    }
}
