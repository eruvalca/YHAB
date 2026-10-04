using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Services;

/// <summary>Account validation and reconciliation inputs, excluding its opening balance.</summary>
internal sealed record AccountLedgerFacts(DateOnly? FirstTransaction, bool HasReconciled, decimal WorkingMovement, decimal ClearedMovement)
{
    public static AccountLedgerFacts FromLedger(PlanSnapshot plan, Guid accountId, DateOnly through)
    {
        var entries = plan.Transactions.Where(item => item.AccountId == accountId || item.TransferAccountId == accountId).ToArray();
        var posted = entries.Where(item => item.Repeat == RepeatFrequency.None && item.Date <= through).ToArray();
        decimal Amount(TransactionData entry) => entry.AccountId == accountId ? entry.Amount : -entry.Amount;
        ClearingState State(TransactionData entry) => entry.AccountId == accountId ? entry.State : entry.TransferState;
        return new(entries.Select(item => (DateOnly?)item.Date).Min(), entries.Any(item => State(item) == ClearingState.Reconciled),
            posted.Sum(Amount), posted.Where(item => State(item) != ClearingState.Uncleared).Sum(Amount));
    }
}
