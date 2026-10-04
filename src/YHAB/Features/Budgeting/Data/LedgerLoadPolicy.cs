using Microsoft.EntityFrameworkCore;
using YHAB.Data;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Data;

internal static class LedgerLoadPolicy
{
    public static bool NeedsLedger(PlanCommand? command) => command is not (AssignMoney or MoveMoney or AutoAssign or SaveGroup or SaveCategory or SaveAccount or UpdatePlan or RenamePayee);
    public static async Task<IQueryable<BudgetTransaction>> SelectAsync(ApplicationDbContext database, Guid planId, PlanCommand? command, CancellationToken token)
    {
        var entries = database.Set<BudgetTransaction>().Where(item => item.PlanId == planId);
        if (command is PostRecurring post)
        {
            var templates = await DueTemplates(database, entries, planId, post.ThroughDate).ToArrayAsync(token);
            return entries.Where(item => templates.Contains(item.Id));
        }
        return command switch
        {
            UpdateTransactionStates change when change.TransactionIds is not null => entries.Where(item => change.TransactionIds.Contains(item.Id)),
            DeleteTransactions change when change.TransactionIds is not null => entries.Where(item => change.TransactionIds.Contains(item.Id)),
            ReconcileAccount change => entries.Where(item => item.Repeat == RepeatFrequency.None && item.Date <= change.Date
                && ((item.AccountId == change.AccountId && item.State == ClearingState.Cleared)
                    || (item.TransferAccountId == change.AccountId && item.TransferState == ClearingState.Cleared))),
            RemoveCategory change => entries.Where(item => database.Set<BudgetSplit>().Any(split => split.PlanId == planId && split.TransactionId == item.Id && split.CategoryId == change.CategoryId)),
            SaveTransaction change when change.Transaction?.Splits is not null => ForSave(database, entries, planId, change.Transaction),
            _ => entries,
        };
    }

    private static IQueryable<BudgetTransaction> ForSave(ApplicationDbContext database, IQueryable<BudgetTransaction> entries, Guid planId, TransactionData entry)
    {
        var splitIds = entry.Splits.Select(item => item.Id).Where(id => id != Guid.Empty).ToArray();
        // Separate indexed lookups avoid an OR/subquery filter that scans every
        // transaction (and can trigger PostgreSQL JIT) for a one-entry edit.
        var ids = entries.Where(item => item.Id == entry.Id).Select(item => item.Id);
        if (splitIds.Length != 0)
        {
            ids = ids.Union(database.Set<BudgetSplit>().Where(item => item.PlanId == planId && splitIds.Contains(item.Id)).Select(item => item.TransactionId));
        }
        return entries.Where(item => ids.Contains(item.Id));
    }

    private static IQueryable<Guid> DueTemplates(ApplicationDbContext database, IQueryable<BudgetTransaction> entries, Guid planId, DateOnly through)
        => entries.Where(item => item.Repeat != RepeatFrequency.None && item.Date <= through
            && !database.Set<BudgetAccount>().Any(account => account.PlanId == planId && account.Closed && (account.Id == item.AccountId || account.Id == item.TransferAccountId)))
            .OrderBy(item => item.Date).ThenBy(item => item.Sequence).ThenBy(item => item.Id).Take(RecurrencePlanner.BatchSize).Select(item => item.Id);
}
