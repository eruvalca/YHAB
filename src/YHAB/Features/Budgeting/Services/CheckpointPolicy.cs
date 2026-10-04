using YHAB.Features.Budgeting.Models;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Services;

/// <summary>Only facts used to advance balances and envelope carry invalidate saved openings.</summary>
internal static class CheckpointPolicy
{
    public static CheckpointInvalidation For(LedgerPatch patch)
    {
        var before = patch.Before;
        var after = patch.After;
        if (!before.Accounts.Select(item => (item.Id, item.Kind, item.OpeningBalance, item.OpenedOn)).OrderBy(item => item.Id)
                .SequenceEqual(after.Accounts.Select(item => (item.Id, item.Kind, item.OpeningBalance, item.OpenedOn)).OrderBy(item => item.Id))
            || !before.Categories.Select(item => (item.Id, item.CreditAccountId)).OrderBy(item => item.Id)
                .SequenceEqual(after.Categories.Select(item => (item.Id, item.CreditAccountId)).OrderBy(item => item.Id)))
        {
            return new ResetCheckpoints();
        }
        var dates = AllocationDates(before, after).Concat(AllocationDates(after, before))
            .Concat(TransactionDates(before, after)).Concat(TransactionDates(after, before)).ToArray();
        return dates.Length == 0 ? new PreserveCheckpoints() : new InvalidateCheckpointsAfter(BudgetFacts.Month(dates.Min()));
    }

    private static IEnumerable<DateOnly> AllocationDates(PlanSnapshot source, PlanSnapshot other)
    {
        var amounts = other.Allocations.ToDictionary(item => (item.CategoryId, item.Month), item => item.Amount);
        return source.Allocations.Where(item => item.Amount != amounts.GetValueOrDefault((item.CategoryId, item.Month))).Select(item => item.Month);
    }

    private static IEnumerable<DateOnly> TransactionDates(PlanSnapshot source, PlanSnapshot other)
    {
        var posted = other.Transactions.Where(item => item.Repeat == RepeatFrequency.None).ToDictionary(item => item.Id);
        return source.Transactions.Where(item => item.Repeat == RepeatFrequency.None
            && (!posted.TryGetValue(item.Id, out var current) || !SameMovement(item, current))).Select(item => item.Date);
    }

    private static bool SameMovement(TransactionData before, TransactionData after)
        => (before.AccountId, before.TransferAccountId, before.Date, before.Sequence, before.Amount)
            == (after.AccountId, after.TransferAccountId, after.Date, after.Sequence, after.Amount)
            && before.Splits.OrderBy(item => item.Id).Select(item => (item.Id, item.CategoryId, item.Amount))
                .SequenceEqual(after.Splits.OrderBy(item => item.Id).Select(item => (item.Id, item.CategoryId, item.Amount)));
}
