using System.Data;
using Microsoft.EntityFrameworkCore;
using YHAB.Data;
using YHAB.Features.Budgeting.Data;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Services;

internal sealed class BudgetQueries(IDbContextFactory<ApplicationDbContext> factory, BudgetClock clock, BudgetMonthCache cache)
{
    public Task<long> RevisionAsync(string userId, Guid id, CancellationToken token)
        => ReadAsync(userId, id, (_, plan) => Task.FromResult(plan.Version), token);

    public Task<PlanView> ViewAsync(string userId, Guid id, CancellationToken token)
        => ReadAsync(userId, id, (database, plan) => ReadViewAsync(database, plan, userId, token), token);

    private async Task<PlanView> ReadViewAsync(ApplicationDbContext database, BudgetPlan plan, string userId, CancellationToken token)
    {
        var id = plan.Id;
        var today = clock.Today;
        var catalog = (await BudgetSnapshotMapping.LoadAsync(database, plan, token, includeLedger: false, includeAllocations: false)) with { Today = today };
        catalog = await PlanAccess.DescribeViewerAsync(database, plan, catalog, userId, token);
        var entries = database.Set<BudgetTransaction>().Where(entry => entry.PlanId == id && entry.Repeat == RepeatFrequency.None && entry.Date <= today);
        var balances = await database.Set<BudgetAccount>().Where(account => account.PlanId == id)
            .Select(account => new
            {
                account.Id,
                Opening = account.OpenedOn <= today ? account.OpeningBalance : 0,
                Movement = entries.Where(entry => entry.AccountId == account.Id).Sum(entry => entry.Amount)
                    - entries.Where(entry => entry.TransferAccountId == account.Id).Sum(entry => entry.Amount),
            }).ToArrayAsync(token);
        var closed = catalog.Accounts.Where(account => account.Closed).Select(account => account.Id).ToArray();
        var due = await database.Set<BudgetTransaction>().AnyAsync(entry => entry.PlanId == id && entry.Repeat != RepeatFrequency.None
            && entry.Date <= today && !closed.Contains(entry.AccountId) && (!entry.TransferAccountId.HasValue || !closed.Contains(entry.TransferAccountId.Value)), token);
        return new PlanView(catalog, balances.Select(item => new AccountBalance(item.Id, item.Opening + item.Movement)).ToArray(), due);
    }

    public async Task<PlanMonthView> WorkspaceAsync(string userId, Guid id, DateOnly? requestedMonth, CancellationToken token)
    {
        if (requestedMonth is { } date && !CatalogChanges.ValidDate(date))
        {
            throw new BudgetRequestException(400, "Choose a month between 2000 and 2100.");
        }
        // The view and calculation share one repeatable-read snapshot. A writer
        // cannot advance the revision between these two projections.
        var result = await ReadAsync(userId, id, async (database, plan) =>
        {
            var view = await ReadViewAsync(database, plan, userId, token);
            var month = BudgetFacts.Month(requestedMonth ?? view.Catalog.Today);
            var key = (userId, id, plan.Version, view.Catalog.Today, month);
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
            await BudgetCheckpoints.SaveAsync(factory, userId, id, catalog.Version, result.States, token);
        }
        cache.Set((userId, id, catalog.Version, catalog.Today, result.View.Month.Month), result.View.Month);
        return result.View;
    }

    public async Task<BudgetMonth> MonthAsync(string userId, Guid id, DateOnly month, long version, CancellationToken token)
    {
        if (!CatalogChanges.ValidDate(month)) { throw new BudgetRequestException(400, "Choose a month between 2000 and 2100."); }
        // Authorize and read the revision even on a cache hit. Entries are bounded, small projections, never ledgers.
        await ReadAsync(userId, id, (_, plan) => { RequireVersion(plan, version); return Task.FromResult(true); }, token);
        month = BudgetFacts.Month(month);
        var key = (userId, id, version, clock.Today, month);
        if (cache.TryGet(key, out var cached)) { return cached; }
        var result = await ReadAsync(userId, id, async (database, plan) =>
        {
            RequireVersion(plan, version);
            var catalog = (await BudgetSnapshotMapping.LoadAsync(database, plan, token, includeLedger: false, includeAllocations: false)) with { Today = clock.Today };
            return await BudgetMonthReplay.ReadAsync(database, catalog, month, token);
        }, token);
        await BudgetCheckpoints.SaveAsync(factory, userId, id, version, result.States, token);
        cache.Set(key, result.Budget);
        return result.Budget;
    }

    public async Task<ReportView> ReportsAsync(string userId, Guid id, DateOnly from, DateOnly through, CancellationToken token)
    {
        if (!CatalogChanges.ValidDate(from) || !CatalogChanges.ValidDate(through) || through < from || through.DayNumber - from.DayNumber > 732)
        {
            throw new BudgetRequestException(400, "Choose an ordered report range of up to two years.");
        }
        return await ReadAsync(userId, id, async (database, plan) =>
        {
            var catalog = (await BudgetSnapshotMapping.LoadAsync(database, plan, token, includeLedger: false, includeAllocations: false)) with { Today = clock.Today };
            return await ReportQueries.ReadAsync(database, catalog, from, through, token);
        }, token);
    }

    public Task<IReadOnlyList<string>> PayeesAsync(string userId, Guid id, CancellationToken token)
        => ReadAsync<IReadOnlyList<string>>(userId, id, async (database, _) => await database.Set<BudgetTransaction>()
            .Where(item => item.PlanId == id && item.Payee != "").Select(item => item.Payee).Distinct().OrderBy(item => item).ToArrayAsync(token), token);

    public Task<RegisterPage> RegisterAsync(string userId, Guid id, RegisterQuery request, CancellationToken token)
        => ReadAsync(userId, id, async (database, plan) =>
        {
            RegisterPolicy.Validate(request);
            if (request.Version is { } version) { RequireVersion(plan, version); }
            return await RegisterQueries.ReadAsync(database, plan.Id, plan.Version, request, token);
        }, token);

    private async Task<T> ReadAsync<T>(string userId, Guid id, Func<ApplicationDbContext, BudgetPlan, Task<T>> read, CancellationToken token)
    {
        await using var strategyContext = await factory.CreateDbContextAsync(token);
        return await strategyContext.Database.CreateExecutionStrategy().ExecuteAsync(async _ =>
        {
            await using var database = await factory.CreateDbContextAsync(token);
            database.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
            var plan = await database.Set<BudgetPlan>().SingleOrDefaultAsync(item => item.Id == id, token);
            if (plan is null || !await PlanAccess.CanAccessAsync(database, plan, userId, token))
            {
                throw PlanAccess.Unavailable();
            }
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
