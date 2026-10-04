using Microsoft.EntityFrameworkCore;
using YHAB.Data;
using YHAB.Features.Budgeting.Data;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Services;

internal sealed class RecurringProcessor(IDbContextFactory<ApplicationDbContext> factory, BudgetStore store, BudgetClock clock, ILogger<RecurringProcessor> logger)
{
    private static readonly Action<ILogger, Guid, Exception?> _planFailed = LoggerMessage.Define<Guid>(LogLevel.Error,
        new EventId(4101, "RecurringPlanFailed"), "Recurring posting failed for plan {PlanId}; other plans will continue.");
    // Discovery is bounded. Execution serializes one batch per plan across processes
    // and refreshes the revision after acquiring the plan lock.
    public async Task<int> RunBatchAsync(CancellationToken cancellationToken)
    {
        await using var database = await factory.CreateDbContextAsync(cancellationToken);
        var today = clock.Today;
        var candidates = await database.Set<BudgetTransaction>().AsNoTracking()
            .Where(entry => entry.Repeat != RepeatFrequency.None && entry.Date <= today
                && !database.Set<BudgetAccount>().Any(account => account.PlanId == entry.PlanId && account.Closed
                    && (account.Id == entry.AccountId || account.Id == entry.TransferAccountId)))
            .GroupBy(entry => entry.PlanId).Select(group => new { PlanId = group.Key, Date = group.Min(entry => entry.Date) })
            .OrderBy(item => item.Date).ThenBy(item => item.PlanId).Take(32)
            .Join(database.Set<BudgetPlan>(), item => item.PlanId, plan => plan.Id, (item, plan) => new { plan.Id, plan.OwnerId })
            .ToListAsync(cancellationToken);
        var committed = 0;
        foreach (var plan in candidates)
        {
            try
            {
                if (await store.PostRecurringAsync(plan.OwnerId, plan.Id, today, cancellationToken))
                {
                    committed++;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Retain the exception and plan identity, without abandoning other discovered plans.
                _planFailed(logger, plan.Id, exception);
            }
        }
        return committed;
    }
}
