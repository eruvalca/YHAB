using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using YHAB.Data;
using YHAB.Features.Budgeting.Data;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Services;

internal static class BudgetCheckpoints
{
    // Increment when the interpretation of a saved opening state changes.
    internal const int FormatVersion = 1;

    public static Task InvalidateAsync(ApplicationDbContext database, Guid planId, long version, LedgerPatch patch, CancellationToken token)
        => CheckpointPolicy.For(patch).Match(_ => Task.CompletedTask,
            _ => InvalidateFromAsync(database, planId, version, DateOnly.MinValue, token),
            changed => InvalidateFromAsync(database, planId, version, changed.Month.AddMonths(1), token));

    private static async Task InvalidateFromAsync(ApplicationDbContext database, Guid planId, long version, DateOnly firstInvalidMonth, CancellationToken token)
    {
        // The caller already holds the plan's write claim. The boundary and
        // checkpoint removal commit or roll back with the financial change.
        await database.Set<BudgetCheckpoint>().Where(item => item.PlanId == planId && item.Month >= firstInvalidMonth).ExecuteDeleteAsync(token);
        // A newer, earlier boundary also protects every older calculation that
        // the removed boundaries protected. Retain only undominated boundaries,
        // bounded by distinct months instead of the number of edits.
        await database.Set<BudgetCheckpointInvalidation>().Where(item => item.PlanId == planId && item.FirstInvalidMonth >= firstInvalidMonth).ExecuteDeleteAsync(token);
        database.Add(new BudgetCheckpointInvalidation { PlanId = planId, Version = version, FirstInvalidMonth = firstInvalidMonth });
    }

    public static async Task SaveAsync(IDbContextFactory<ApplicationDbContext> factory, string owner, Guid id, long version,
        IReadOnlyList<BudgetMonthState> states, CancellationToken token)
    {
        await using var strategyContext = await factory.CreateDbContextAsync(token);
        await strategyContext.Database.CreateExecutionStrategy().ExecuteAsync(async _ =>
        {
            await using var database = await factory.CreateDbContextAsync(token);
            await using var transaction = await database.Database.BeginTransactionAsync(token);
            // Serialize publication with financial invalidation without changing
            // the plan revision. A stale calculation can still publish openings
            // preceding every financial change made since its snapshot.
            var claimed = await database.Set<BudgetPlan>().Where(item => item.Id == id && item.OwnerId == owner && item.Version >= version)
                .ExecuteUpdateAsync(update => update.SetProperty(item => item.Version, item => item.Version), token);
            if (claimed == 0) { return; }
            var firstInvalidMonth = await database.Set<BudgetCheckpointInvalidation>().Where(item => item.PlanId == id && item.Version > version)
                .Select(item => (DateOnly?)item.FirstInvalidMonth).MinAsync(token);
            var valid = states.Where(state => firstInvalidMonth is null || state.Month < firstInvalidMonth.Value).ToArray();
            await database.Set<BudgetCheckpoint>().Where(item => item.PlanId == id && item.FormatVersion != FormatVersion).ExecuteDeleteAsync(token);
            var months = valid.Select(item => item.Month).ToArray();
            var existing = (await database.Set<BudgetCheckpoint>().Where(item => item.PlanId == id && months.Contains(item.Month)).Select(item => item.Month).ToArrayAsync(token)).ToHashSet();
            foreach (var state in valid.Where(item => !existing.Contains(item.Month)))
            {
                database.Add(new BudgetCheckpoint { PlanId = id, Month = state.Month, FormatVersion = FormatVersion, State = JsonSerializer.Serialize(state) });
            }
            await database.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
        }, token);
    }
}
