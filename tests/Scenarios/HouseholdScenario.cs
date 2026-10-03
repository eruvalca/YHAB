using YHAB.SharedKernel.Budgeting;

namespace YHAB.Tests.Scenarios;

// Linked source keeps the deterministic fixture identical across headless test layers.
// Expected totals are documented independently in docs/budgeting-validation.md.
internal static class HouseholdScenario
{
    public static DateOnly Start => new(2026, 1, 1);
    public static Guid Id(int value) => new(value, 0, 0, new byte[8]);
    public static DateOnly End(int months) => Start.AddMonths(months).AddDays(-1);

    public static PlanSnapshot Create(int months, int monthlyCount)
    {
        AccountData[] accounts =
        [
            new(Id(1), "Checking", AccountKind.Checking, 6000, Start, false, ""),
            new(Id(2), "Savings", AccountKind.Savings, 8000, Start, false, ""),
            new(Id(3), "Everyday card", AccountKind.CreditCard, -600, Start, false, ""),
            new(Id(4), "Rewards card", AccountKind.CreditCard, 0, Start, false, ""),
            new(Id(5), "Car value", AccountKind.Asset, 15000, Start, false, ""),
            new(Id(6), "Car loan", AccountKind.AutoLoan, -12000, Start, false, "", 5, 250),
        ];
        string[] names = ["Housing", "Utilities", "Groceries", "Dining", "Transport", "Annual bills", "Fun", "Loan payments", "Everyday payment", "Rewards payment"];
        decimal[] assigned = [1800, 300, 850, 400, 350, 150, 250, 250];
        var categories = names.Select((name, index) => new CategoryData(Id(100 + index), Id(50), name, "", index, false,
            index >= 8 ? Id(index - 5) : null,
            index == 5 ? new(TargetKind.SetAside, TargetCadence.Custom, 900, Start, new(2026, 6, 30)) : null)).ToArray();
        var allocations = new List<AllocationData> { new(Id(108), Start, 600) };
        var transactions = new List<TransactionData>();
        for (var month = 0; month < months; month++)
        {
            var date = Start.AddMonths(month);
            allocations.AddRange(assigned.Select((amount, index) => new AllocationData(Id(100 + index), date, amount)));
            AddMonth(transactions, month, monthlyCount);
        }

        return new(Id(99), "Three month household", "Synthetic validation data", Start, 0, accounts,
            [new(Id(50), "Household", 0)], categories, allocations, transactions, false, false, [])
        {
            Today = End(months),
        };
    }

    private static void AddMonth(List<TransactionData> entries, int month, int count)
    {
        var sequence = (month + 1) * 10000;
        var first = Start.AddMonths(month);
        void Add(int day, int account, string payee, decimal amount, int? category = null, int? transfer = null,
            int? secondCategory = null, decimal secondAmount = 0)
        {
            var key = ++sequence;
            var splits = new List<SplitData>();
            if (category.HasValue || (transfer is null && account != 6))
            {
                splits.Add(new(Id(1000000 + key * 2), category.HasValue ? Id(category.Value) : null, amount - secondAmount, ""));
                if (secondCategory.HasValue)
                {
                    splits.Add(new(Id(1000001 + key * 2), Id(secondCategory.Value), secondAmount, "Split purchase"));
                }
            }

            entries.Add(new(Id(key), Id(account), first.AddDays(day - 1), payee, "Synthetic household ledger", amount,
                transfer.HasValue ? Id(transfer.Value) : null, ClearingState.Cleared, ClearingState.Cleared, false, "", splits));
        }

        Add(1, 1, "Salary", 4200);
        Add(15, 1, "Salary", 4200);
        Add(1, 1, "Rent", -1800, 100);
        Add(10, 1, "Utilities", -270, 101);
        Add(20, 1, "Loan payment", -250, 107, 6);
        Add(21, 6, "Loan interest", -50);
        Add(16, 1, "Savings transfer", -600, transfer: 2);
        Add(25, 1, "Household split", -100, 102, secondCategory: 106, secondAmount: -40);
        Add(25, 4, "Rewards split", -150, 103, secondCategory: 106, secondAmount: -100);
        Add(26, 3, "Grocery refund", 50, 102);
        Add(28, 1, "Everyday payment", month == 0 ? -1250 : -650, transfer: 3);
        Add(28, 1, "Rewards payment", -150, transfer: 4);

        (int Account, int Category, int Cents, string Payee)[] buckets =
        [
            (1, 102, 40000, "Market"), (1, 103, 10000, "Cafe"), (1, 104, 20000, "Transit"),
            (3, 102, 40000, "Card market"), (3, 103, 20000, "Restaurant"), (3, 104, 10000, "Fuel"),
        ];
        for (var bucket = 0; bucket < buckets.Length; bucket++)
        {
            var purchases = (count - 12) / buckets.Length + (bucket < (count - 12) % buckets.Length ? 1 : 0);
            var item = buckets[bucket];
            for (var index = 0; index < purchases; index++)
            {
                var cents = item.Cents / purchases + (index < item.Cents % purchases ? 1 : 0);
                Add(2 + index % 23, item.Account, $"{item.Payee} {index % 7 + 1}", -cents / 100m, item.Category);
            }
        }
    }
}
