using Microsoft.EntityFrameworkCore;
using YHAB.Data;
using YHAB.Features.Budgeting.Data;
using YHAB.Features.Budgeting.Models;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Services;

internal static class BudgetHistoryReader
{
    public static async Task<Input> ReadAsync(ApplicationDbContext database,
        BudgetPlan plan, bool reverse, CancellationToken token)
    {
        var current = await BudgetSnapshotMapping.LoadAsync(database, plan, token, includeLedger: false, includeAllocations: false);
        var position = HistoryPolicy.RequestedPosition(plan.HistoryCursor, !reverse);
        var history = await database.Set<BudgetHistory>().SingleOrDefaultAsync(item => item.PlanId == plan.Id && item.Position == position, token);
        if (history is null) { return new(current, null); }
        return await BudgetHistoryCodec.Deserialize(history).Match(async patch =>
        {
            var expected = reverse ? patch.After : patch.Before;
            var desired = reverse ? patch.Before : patch.After;
            var removedAccounts = expected.Accounts.Select(item => item.Id).Except(desired.Accounts.Select(item => item.Id)).ToArray();
            var removedCategories = expected.Categories.Select(item => item.Id).Except(desired.Categories.Select(item => item.Id)).ToArray();
            var allocations = await AllocationsAsync(database, plan.Id, patch, removedCategories, token);
            var entries = await EntriesAsync(database, plan.Id, patch, removedAccounts, removedCategories, token);
            var snapshot = current with { Allocations = allocations, Transactions = entries };
            return new Input(snapshot, NormalizeDates(patch, snapshot));
        }, payees => Task.FromResult(new Input(current, payees)));
    }

    internal sealed record Input(PlanSnapshot Current, BudgetMutation? Patch);

    private static async Task<AllocationData[]> AllocationsAsync(ApplicationDbContext database, Guid planId, LedgerPatch patch,
        Guid[] removedCategories, CancellationToken token)
    {
        var keys = patch.Before.Allocations.Concat(patch.After.Allocations).Select(item => (item.CategoryId, item.Month)).Distinct().ToArray();
        if (keys.Length + removedCategories.Length == 0) { return []; }
        var categories = keys.Select(item => item.CategoryId).ToArray();
        var months = keys.Select(item => item.Month).ToArray();
        var allocations = await database.Set<BudgetAllocation>().FromSql($"""
            SELECT a.* FROM "BudgetAllocation" AS a
            WHERE a."PlanId" = {planId} AND (a."CategoryId" = ANY({removedCategories}) OR EXISTS (
                SELECT 1 FROM unnest({categories}, {months}) AS k("Id", "Month")
                WHERE k."Id" = a."CategoryId" AND k."Month" = a."Month"))
            """).ToArrayAsync(token);
        return allocations.Select(item => new AllocationData(item.CategoryId, item.Month, item.Amount, item.Snoozed)).ToArray();
    }

    private static async Task<TransactionData[]> EntriesAsync(ApplicationDbContext database, Guid planId, LedgerPatch patch,
        Guid[] removedAccounts, Guid[] removedCategories, CancellationToken token)
    {
        var changed = patch.Before.Transactions.Concat(patch.After.Transactions).Select(item => item.Id).Distinct().ToArray();
        if (changed.Length + removedAccounts.Length + removedCategories.Length == 0) { return []; }
        var ledger = database.Set<BudgetTransaction>().Where(item => item.PlanId == planId);
        var ids = ledger.Where(item => changed.Contains(item.Id)).Select(item => item.Id);
        if (removedAccounts.Length != 0)
        {
            ids = ids.Union(ledger.Where(item => removedAccounts.Contains(item.AccountId)
                || (item.TransferAccountId.HasValue && removedAccounts.Contains(item.TransferAccountId.Value))).Select(item => item.Id));
        }
        if (removedCategories.Length != 0)
        {
            ids = ids.Union(database.Set<BudgetSplit>().Where(item => item.PlanId == planId && item.CategoryId.HasValue
                && removedCategories.Contains(item.CategoryId.Value)).Select(item => item.TransactionId));
        }
        var entries = await ledger.Where(item => ids.Contains(item.Id)).ToArrayAsync(token);
        var splits = (await database.Set<BudgetSplit>().Where(item => item.PlanId == planId && ids.Contains(item.TransactionId)).ToArrayAsync(token))
            .ToLookup(item => item.TransactionId);
        return entries.Select(item => BudgetSnapshotMapping.ToData(item, splits[item.Id].Select(BudgetSnapshotMapping.ToData).ToArray())).ToArray();
    }

    private static LedgerPatch NormalizeDates(LedgerPatch patch, PlanSnapshot current)
    {
        var legacy = patch.Before.Transactions.Concat(patch.After.Transactions)
            .Where(item => item.SourceTemplateId.HasValue && !item.ScheduledDate.HasValue).Select(item => item.Id).ToHashSet();
        if (legacy.Count == 0) { return patch; }
        // Preserve occurrence identities from pre-ScheduledDate history, including
        // entries whose editable date changed after they were posted.
        var dates = patch.Before.Transactions.Concat(patch.After.Transactions).Concat(current.Transactions)
            .Where(item => legacy.Contains(item.Id) && item.SourceTemplateId.HasValue).GroupBy(item => item.Id)
            .ToDictionary(group => group.Key, group => group.LastOrDefault(item => item.ScheduledDate.HasValue)?.ScheduledDate ?? group.Last().Date);
        return LedgerPatch.Between(Normalize(patch.Before, dates), Normalize(patch.After, dates));
    }

    private static PlanSnapshot Normalize(PlanSnapshot snapshot, Dictionary<Guid, DateOnly> dates)
        => snapshot with
        {
            Transactions = snapshot.Transactions.Select(item => item.SourceTemplateId.HasValue && !item.ScheduledDate.HasValue
                ? item with { ScheduledDate = dates[item.Id] } : item).ToArray(),
        };
}
