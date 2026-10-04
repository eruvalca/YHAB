using System.Data;
using Microsoft.EntityFrameworkCore;
using YHAB.Data;
using YHAB.Features.Budgeting.Data;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Services;

internal sealed class BudgetQueries(IDbContextFactory<ApplicationDbContext> factory, BudgetClock clock, BudgetMonthCache cache)
{
    public Task<long> RevisionAsync(string owner, Guid id, CancellationToken token)
        => ReadAsync(owner, id, (_, plan) => Task.FromResult(plan.Version), token);

    public Task<PlanView> ViewAsync(string owner, Guid id, CancellationToken token)
        => ReadAsync(owner, id, (database, plan) => ReadViewAsync(database, plan, token), token);

    private async Task<PlanView> ReadViewAsync(ApplicationDbContext database, BudgetPlan plan, CancellationToken token)
    {
        var id = plan.Id;
        var today = clock.Today;
        var catalog = (await BudgetSnapshotMapping.LoadAsync(database, plan, token, includeLedger: false, includeAllocations: false)) with { Today = today };
        var entries = database.Set<BudgetTransaction>().Where(entry => entry.PlanId == id && entry.Repeat == RepeatFrequency.None && entry.Date <= today);
        var balances = await database.Set<BudgetAccount>().Where(account => account.PlanId == id)
            .Select(account => new
            {
                account.Id,
                Opening = account.OpenedOn <= today ? account.OpeningBalance : 0,
                Cleared = entries.Where(entry => entry.AccountId == account.Id && entry.State != ClearingState.Uncleared).Sum(entry => entry.Amount)
                    - entries.Where(entry => entry.TransferAccountId == account.Id && entry.TransferState != ClearingState.Uncleared).Sum(entry => entry.Amount),
                Uncleared = entries.Where(entry => entry.AccountId == account.Id && entry.State == ClearingState.Uncleared).Sum(entry => entry.Amount)
                    - entries.Where(entry => entry.TransferAccountId == account.Id && entry.TransferState == ClearingState.Uncleared).Sum(entry => entry.Amount),
                Reconciled = entries.Where(entry => entry.AccountId == account.Id && entry.State == ClearingState.Reconciled).Sum(entry => entry.Amount)
                    - entries.Where(entry => entry.TransferAccountId == account.Id && entry.TransferState == ClearingState.Reconciled).Sum(entry => entry.Amount),
            }).ToArrayAsync(token);
        var closed = catalog.Accounts.Where(account => account.Closed).Select(account => account.Id).ToArray();
        var due = await database.Set<BudgetTransaction>().AnyAsync(entry => entry.PlanId == id && entry.Repeat != RepeatFrequency.None
            && entry.Date <= today && !closed.Contains(entry.AccountId) && (!entry.TransferAccountId.HasValue || !closed.Contains(entry.TransferAccountId.Value)), token);
        return new PlanView(catalog, balances.Select(item => new AccountBalance(item.Id, item.Opening + item.Cleared,
            item.Uncleared, item.Reconciled, item.Opening + item.Cleared + item.Uncleared)).ToArray(), due);
    }

    public async Task<PlanMonthView> WorkspaceAsync(string owner, Guid id, DateOnly? requestedMonth, CancellationToken token)
    {
        if (requestedMonth is { } date && !CatalogChanges.ValidDate(date))
        {
            throw new BudgetRequestException(400, "Choose a month between 2000 and 2100.");
        }
        // The view and calculation share one repeatable-read snapshot. A writer
        // cannot advance the revision between these two projections.
        var result = await ReadAsync(owner, id, async (database, plan) =>
        {
            var view = await ReadViewAsync(database, plan, token);
            var month = BudgetFacts.Month(requestedMonth ?? view.Catalog.Today);
            var key = (owner, id, plan.Version, view.Catalog.Today, month);
            if (cache.TryGet(key, out var cached))
            {
                return (View: new PlanMonthView(view, cached), States: (IReadOnlyList<BudgetMonthState>)Array.Empty<BudgetMonthState>());
            }
            var replay = await BudgetMonthReplay.ReadAsync(database, view.Catalog, month, token);
            return (View: new PlanMonthView(view, replay.Budget), States: replay.States);
        }, token);
        var catalog = result.View.View.Catalog;
        if (result.States.Count > 0)
        {
            await BudgetCheckpoints.SaveAsync(factory, owner, id, catalog.Version, result.States, token);
        }
        cache.Set((owner, id, catalog.Version, catalog.Today, result.View.Month.Month), result.View.Month);
        return result.View;
    }

    public async Task<BudgetMonth> MonthAsync(string owner, Guid id, DateOnly month, long version, CancellationToken token)
    {
        if (!CatalogChanges.ValidDate(month)) { throw new BudgetRequestException(400, "Choose a month between 2000 and 2100."); }
        // Authorize and read the revision even on a cache hit. Entries are bounded, small projections, never ledgers.
        await ReadAsync(owner, id, (_, plan) => { RequireVersion(plan, version); return Task.FromResult(true); }, token);
        month = BudgetFacts.Month(month);
        var key = (owner, id, version, clock.Today, month);
        if (cache.TryGet(key, out var cached)) { return cached; }
        var result = await ReadAsync(owner, id, async (database, plan) =>
        {
            RequireVersion(plan, version);
            var catalog = (await BudgetSnapshotMapping.LoadAsync(database, plan, token, includeLedger: false, includeAllocations: false)) with { Today = clock.Today };
            return await BudgetMonthReplay.ReadAsync(database, catalog, month, token);
        }, token);
        await BudgetCheckpoints.SaveAsync(factory, owner, id, version, result.States, token);
        cache.Set(key, result.Budget);
        return result.Budget;
    }

    public async Task<ReportView> ReportsAsync(string owner, Guid id, DateOnly from, DateOnly through, CancellationToken token)
    {
        if (!CatalogChanges.ValidDate(from) || !CatalogChanges.ValidDate(through) || through < from || through.DayNumber - from.DayNumber > 732)
        {
            throw new BudgetRequestException(400, "Choose an ordered report range of up to two years.");
        }
        return await ReadAsync(owner, id, async (database, plan) =>
        {
            var catalog = (await BudgetSnapshotMapping.LoadAsync(database, plan, token, includeLedger: false, includeAllocations: false)) with { Today = clock.Today };
            return await ReportQueries.ReadAsync(database, catalog, from, through, token);
        }, token);
    }

    public Task<IReadOnlyList<string>> PayeesAsync(string owner, Guid id, CancellationToken token)
        => ReadAsync<IReadOnlyList<string>>(owner, id, async (database, _) => await database.Set<BudgetTransaction>()
            .Where(item => item.PlanId == id && item.Payee != "").Select(item => item.Payee).Distinct().OrderBy(item => item).ToArrayAsync(token), token);

    public Task<RegisterPage> RegisterAsync(string owner, Guid id, RegisterQuery request, CancellationToken token)
        => ReadAsync(owner, id, async (database, plan) =>
        {
            RegisterPolicy.Validate(request);
            if (request.Version is { } version) { RequireVersion(plan, version); }
            return await RegisterQueries.ReadAsync(database, plan.Id, plan.Version, request, token);
        }, token);

    private async Task<T> ReadAsync<T>(string owner, Guid id, Func<ApplicationDbContext, BudgetPlan, Task<T>> read, CancellationToken token)
    {
        await using var strategyContext = await factory.CreateDbContextAsync(token);
        return await strategyContext.Database.CreateExecutionStrategy().ExecuteAsync(async _ =>
        {
            await using var database = await factory.CreateDbContextAsync(token);
            database.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
            var plan = await database.Set<BudgetPlan>().SingleOrDefaultAsync(item => item.Id == id && item.OwnerId == owner, token)
                ?? throw new BudgetRequestException(404, "This plan is not available.");
            var result = await read(database, plan);
            await transaction.CommitAsync(token);
            return result;
        }, token);
    }

    private static void RequireVersion(BudgetPlan plan, long version)
    {
        if (plan.Version != version) { throw new BudgetRequestException(409, "This plan changed. Refresh to use its latest balances."); }
    }
}
