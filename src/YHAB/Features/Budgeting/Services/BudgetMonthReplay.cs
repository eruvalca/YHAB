using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using YHAB.Data;
using YHAB.Features.Budgeting.Data;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Services;

internal static class BudgetMonthReplay
{
    public static async Task<(BudgetMonth Budget, IReadOnlyList<BudgetMonthState> States)> ReadAsync(
        ApplicationDbContext database, PlanSnapshot catalog, DateOnly month, CancellationToken token)
    {
        var periods = catalog.Categories.Where(item => item.Target?.Kind is TargetKind.Refill or TargetKind.SetAside)
            .Select(item => (item.Id, item.Target!.Kind, Period: TargetCalculator.PeriodFor(item.Target, month)))
            .Where(item => item.Period is not null).ToArray();
        var required = periods.Where(item => item.Kind == TargetKind.Refill && item.Period!.Start < month)
            .Select(item => item.Period!.Start).Distinct().ToArray();
        var checkpoints = database.Set<BudgetCheckpoint>().Where(item => item.PlanId == catalog.Id && item.FormatVersion == BudgetCheckpoints.FormatVersion);
        var saved = await checkpoints.Where(item => item.Month <= month).OrderByDescending(item => item.Month).FirstOrDefaultAsync(token);
        var starts = new Dictionary<DateOnly, BudgetMonthState>();
        if (required.Length != 0)
        {
            foreach (var checkpoint in await checkpoints.Where(item => required.Contains(item.Month)).ToArrayAsync(token))
            {
                starts.Add(checkpoint.Month, Deserialize(checkpoint));
            }
        }
        var opening = saved is null ? await InitialAsync(database, catalog, month, token) : Deserialize(saved);
        // A partial/old checkpoint cache may lack a target's period opening. Replay
        // from before that opening rather than silently treating its carry as zero.
        var missing = required.Where(item => item < opening.Month && !starts.ContainsKey(item)).ToArray();
        if (missing.Length != 0)
        {
            var initial = await InitialAsync(database, catalog, month, token);
            var replayFrom = missing.Where(item => item >= initial.Month).Select(item => (DateOnly?)item).Min();
            if (replayFrom.HasValue)
            {
                saved = await checkpoints.Where(item => item.Month <= replayFrom.Value).OrderByDescending(item => item.Month).FirstOrDefaultAsync(token);
                opening = saved is null ? initial : Deserialize(saved);
            }
        }
        var ids = periods.Select(item => item.Id).ToArray();
        var periodStarts = periods.Select(item => item.Period!.Start).ToArray();
        var funding = await FundingAsync(database, catalog.Id, month, ids, periodStarts, token);
        return await ReplayAsync(database, catalog, opening, month, starts, required, funding, token);
    }

    private static BudgetMonthState Deserialize(BudgetCheckpoint checkpoint)
        => JsonSerializer.Deserialize<BudgetMonthState>(checkpoint.State)!;

    private static async Task<BudgetMonthState> InitialAsync(ApplicationDbContext database, PlanSnapshot catalog, DateOnly month, CancellationToken token)
    {
        var first = await database.Set<BudgetAllocation>().Where(item => item.PlanId == catalog.Id).Select(item => (DateOnly?)item.Month)
            .Concat(database.Set<BudgetTransaction>().Where(item => item.PlanId == catalog.Id && item.Repeat == RepeatFrequency.None).Select(item => (DateOnly?)item.Date)).MinAsync(token);
        return BudgetCalculator.Start(catalog, first is { } date && date < month ? date : month);
    }

    private static async Task<BudgetMonthFunding> FundingAsync(ApplicationDbContext database, Guid planId, DateOnly month,
        Guid[] ids, DateOnly[] starts, CancellationToken token)
    {
        var future = await database.Set<BudgetAllocation>().Where(item => item.PlanId == planId && item.Month > month).SumAsync(item => item.Amount, token);
        if (ids.Length == 0) { return new(future, ImmutableDictionary<Guid, decimal>.Empty); }
        // One aggregate query for all target periods, even when every category has
        // a different start. Array parameters keep values out of SQL text.
        var progress = await database.Database.SqlQuery<TargetContribution>($"""
            SELECT a."CategoryId", SUM(a."Amount") AS "Amount"
            FROM "BudgetAllocation" AS a
            JOIN unnest({ids}, {starts}) AS p("Id", "Start") ON a."CategoryId" = p."Id"
            WHERE a."PlanId" = {planId} AND a."Month" >= p."Start" AND a."Month" < {month}
            GROUP BY a."CategoryId"
            """).ToArrayAsync(token);
        return new(future, progress.ToImmutableDictionary(item => item.CategoryId, item => item.Amount));
    }

    private static async Task<(BudgetMonth Budget, IReadOnlyList<BudgetMonthState> States)> ReplayAsync(ApplicationDbContext database,
        PlanSnapshot catalog, BudgetMonthState opening, DateOnly month, Dictionary<DateOnly, BudgetMonthState> starts,
        DateOnly[] required, BudgetMonthFunding funding, CancellationToken token)
    {
        var generated = new List<BudgetMonthState> { opening };
        BudgetMonthTransition transition;
        do
        {
            var end = opening.Month.AddMonths(12);
            if (end > month) { end = month.AddMonths(1); }
            var entries = (await BudgetSnapshotMapping.ReadLedgerAsync(database, catalog.Id, opening.Month, end.AddDays(-1), token))
                .ToLookup(item => BudgetFacts.Month(item.Date));
            var allocations = (await database.Set<BudgetAllocation>().Where(item => item.PlanId == catalog.Id && item.Month >= opening.Month && item.Month < end)
                .ToArrayAsync(token)).ToLookup(item => item.Month);
            do
            {
                if (required.Contains(opening.Month)) { starts[opening.Month] = opening; }
                var current = catalog with
                {
                    Transactions = entries[opening.Month].ToArray(),
                    Allocations = allocations[opening.Month].Select(item => new AllocationData(item.CategoryId, item.Month, item.Amount, item.Snoozed)).ToArray(),
                };
                // Intermediate target totals/RTA are not persisted in an opening.
                // The final month alone uses the requested funding aggregates.
                transition = BudgetCalculator.Advance(current, opening, catalog.Today, starts, funding, includeTargets: opening.Month == month);
                opening = transition.Next;
                generated.Add(opening);
            } while (opening.Month < end);
        } while (opening.Month <= month);
        return (transition.Budget, generated);
    }

    private sealed record TargetContribution(Guid CategoryId, decimal Amount);
}
