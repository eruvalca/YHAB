using System.Globalization;

namespace YHAB.SharedKernel.Budgeting;

public static class BudgetFacts
{
    public static CultureInfo Culture { get; } = CultureInfo.GetCultureInfo("en-US");

    public static string Money(decimal value) => value.ToString("C2", Culture);

    public static DateOnly Month(DateOnly date) => new(date.Year, date.Month, 1);

    public static bool IsCash(AccountKind kind) => kind is AccountKind.Checking or AccountKind.Savings or AccountKind.Cash;

    public static bool IsCredit(AccountKind kind) => kind is AccountKind.CreditCard or AccountKind.LineOfCredit;

    public static bool IsBudget(AccountKind kind) => IsCash(kind) || IsCredit(kind);

    public static bool IsLoan(AccountKind kind) => kind is AccountKind.Mortgage or AccountKind.AutoLoan or AccountKind.StudentLoan
        or AccountKind.PersonalLoan or AccountKind.MedicalDebt or AccountKind.OtherDebt;

    public static string AccountLabel(AccountKind kind) => kind switch
    {
        AccountKind.CreditCard => "Credit card",
        AccountKind.LineOfCredit => "Line of credit",
        AccountKind.AutoLoan => "Auto loan",
        AccountKind.StudentLoan => "Student loan",
        AccountKind.PersonalLoan => "Personal loan",
        AccountKind.MedicalDebt => "Medical debt",
        AccountKind.OtherDebt => "Other debt",
        _ => kind.ToString(),
    };

    public static AccountBalance Balance(PlanSnapshot plan, AccountData account, DateOnly through)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(account);
        var cleared = account.OpenedOn <= through ? account.OpeningBalance : 0;
        var uncleared = 0m;
        var reconciled = 0m;
        foreach (var entry in plan.Transactions.Where(entry => entry.Repeat == RepeatFrequency.None && entry.Date <= through))
        {
            var transferAmount = entry.TransferAccountId == account.Id ? -entry.Amount : 0;
            var amount = entry.AccountId == account.Id ? entry.Amount : transferAmount;
            var state = entry.AccountId == account.Id ? entry.State : entry.TransferState;
            if (state == ClearingState.Uncleared)
            {
                uncleared += amount;
            }
            else
            {
                cleared += amount;
                if (state == ClearingState.Reconciled)
                {
                    reconciled += amount;
                }
            }
        }

        return new(account.Id, cleared, uncleared, reconciled, cleared + uncleared);
    }
}
