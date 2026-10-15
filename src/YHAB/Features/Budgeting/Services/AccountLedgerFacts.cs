using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Services;

/// <summary>Account validation inputs, excluding its opening balance.</summary>
internal sealed record AccountLedgerFacts(DateOnly? FirstTransaction, decimal WorkingMovement)
{
    public static AccountLedgerFacts FromLedger(PlanSnapshot plan, Guid accountId, DateOnly through)
    {
        var entries = plan.Transactions.Where(item => item.AccountId == accountId || item.TransferAccountId == accountId).ToArray();
        var posted = entries.Where(item => item.Repeat == RepeatFrequency.None && item.Date <= through).ToArray();
        decimal Amount(TransactionData entry) => entry.AccountId == accountId ? entry.Amount : -entry.Amount;
        return new(entries.Select(item => (DateOnly?)item.Date).Min(), posted.Sum(Amount));
    }
}
