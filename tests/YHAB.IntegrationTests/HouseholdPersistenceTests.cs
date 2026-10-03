using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;
using YHAB.Features.Budgeting.Data;
using YHAB.Features.Budgeting.Services;
using YHAB.SharedKernel.Budgeting;
using YHAB.Tests.Scenarios;

namespace YHAB.IntegrationTests;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class HouseholdPersistenceTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(200)]
    [InlineData(1000)]
    public async Task GrowingLedgerPreservesMathMutationsAndHistoryAtEveryMonthAsync(int monthlyCount)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(6));
        var clock = new ScenarioClock();
        await using var database = await BudgetDatabase.CreateAsync(timeout.Token, clock);
        for (var months = 1; months <= 3; months++)
        {
            clock.Today = HouseholdScenario.End(months);
            var expected = HouseholdScenario.Create(months, monthlyCount);
            await SeedCheckpointAsync(database, expected, timeout.Token);
            var timer = Stopwatch.StartNew();
            var plan = (await database.Store.ReadAsync("owner-a", expected.Id, timeout.Token)).ShouldNotBeNull();
            HouseholdChecks.Verify(plan, months, monthlyCount);
            plan.Today.ShouldBe(clock.Today);
            output.WriteLine($"{months} months / {plan.Transactions.Count} entries: fresh-context read and arithmetic {timer.ElapsedMilliseconds} ms.");
            timer.Restart();
            await VerifyMutationsAsync(database.Store, plan, months, monthlyCount, timeout.Token);
            output.WriteLine($"{months} months / {plan.Transactions.Count} entries: mutation, undo/redo, merge, reconciliation and isolation probes {timer.ElapsedMilliseconds} ms.");
        }
    }

    // Bulk-load only disposable fixtures; every behavior probe uses the real revisioned store.
    // Reset history between fixture checkpoints so snapshots never refer to an external seed.
    private static async Task SeedCheckpointAsync(BudgetDatabase database, PlanSnapshot expected, CancellationToken token)
    {
        await using var context = await database.Factory.CreateDbContextAsync(token);
        var entity = await context.Set<BudgetPlan>().SingleOrDefaultAsync(item => item.Id == expected.Id, token);
        if (entity is null)
        {
            entity = new() { Id = expected.Id, OwnerId = "owner-a", Name = expected.Name, Notes = expected.Notes, CreatedOn = expected.CreatedOn };
            context.Add(entity);
        }
        else
        {
            await BudgetSnapshotMapping.LoadAsync(context, entity, token);
            context.RemoveRange(await context.Set<BudgetHistory>().Where(item => item.PlanId == expected.Id).ToListAsync(token));
            entity.Version = 0;
            entity.HistoryCursor = 0;
        }

        BudgetSnapshotMapping.Apply(context, expected);
        await context.SaveChangesAsync(token);
    }

    private static async Task VerifyMutationsAsync(BudgetStore store, PlanSnapshot original, int months, int monthlyCount, CancellationToken token)
    {
        var month = HouseholdScenario.Start.AddMonths(months - 1);
        var groceries = HouseholdScenario.Id(102);
        var dining = HouseholdScenario.Id(103);
        var plan = await ApplyAsync(store, original, new MoveMoney(original.Version, groceries, dining, month, 10), token);
        var budget = BudgetCalculator.Calculate(plan, month, plan.Today);
        budget.Categories.Single(item => item.Category.Id == groceries).Available.ShouldBe(40 * months - 10);
        budget.Categories.Single(item => item.Category.Id == dining).Available.ShouldBe(50 * months + 10);
        budget.ReadyToAssign.ShouldBe(13400 + 4050 * months);
        plan = await ApplyAsync(store, plan, new UndoChange(plan.Version), token);
        HouseholdChecks.Verify(plan, months, monthlyCount);
        plan = await ApplyAsync(store, plan, new RedoChange(plan.Version), token);
        BudgetCalculator.Calculate(plan, month, plan.Today).Categories.Single(item => item.Category.Id == groceries).Available.ShouldBe(40 * months - 10);
        plan.CanRedo.ShouldBeFalse();
        plan = await ApplyAsync(store, plan, new UndoChange(plan.Version), token);

        var entry = plan.Transactions.First(item => item.Splits.Count == 2 && item.AccountId == HouseholdScenario.Id(1));
        plan = await ApplyAsync(store, plan, new SaveTransaction(plan.Version, entry with { Memo = "Verified split memo" }), token);
        plan.CanRedo.ShouldBeFalse();
        plan.Transactions.Single(item => item.Id == entry.Id).Memo.ShouldBe("Verified split memo");
        plan.Transactions.Single(item => item.Id == entry.Id).Splits.ShouldBe(entry.Splits);
        (await store.ExecuteAsync("owner-a", plan.Id, new DeleteTransactions(original.Version, [entry.Id]), token)).IsT3.ShouldBeTrue();
        (await store.ReadAsync("owner-b", plan.Id, token)).ShouldBeNull();
        (await store.ExecuteAsync("owner-b", plan.Id, new DeleteTransactions(plan.Version, [entry.Id]), token)).IsT2.ShouldBeTrue();

        plan = await ApplyAsync(store, plan, new RemoveCategory(plan.Version, dining, groceries), token);
        plan.Categories.ShouldNotContain(item => item.Id == dining);
        plan.Transactions.SelectMany(item => item.Splits).ShouldNotContain(item => item.CategoryId == dining);
        ReportCalculator.Spending(plan, HouseholdScenario.Start, plan.Today).Single(item => item.CategoryId == groceries).Amount.ShouldBe(1160 * months);
        plan = await ApplyAsync(store, plan, new UndoChange(plan.Version), token);
        HouseholdChecks.Verify(plan, months, monthlyCount);

        plan = await ApplyAsync(store, plan, new ReconcileAccount(plan.Version, HouseholdScenario.Id(1), plan.Today, 5400 + 3880 * months, false), token);
        plan.Transactions.Where(item => item.AccountId == HouseholdScenario.Id(1)).ShouldAllBe(item => item.State == ClearingState.Reconciled);
        (await store.ExecuteAsync("owner-a", plan.Id, new DeleteTransactions(plan.Version, [entry.Id]), token)).IsT1.ShouldBeTrue();
        var reloaded = (await store.ReadAsync("owner-a", plan.Id, token)).ShouldNotBeNull();
        reloaded.Version.ShouldBe(plan.Version);
        HouseholdChecks.Verify(reloaded, months, monthlyCount);
    }

    private static async Task<PlanSnapshot> ApplyAsync(BudgetStore store, PlanSnapshot plan, PlanCommand command, CancellationToken token)
    {
        var result = await store.ExecuteAsync("owner-a", plan.Id, command, token);
        result.IsT0.ShouldBeTrue($"Expected success for {command.GetType().Name}; outcome: {result.Value}");
        var saved = (await store.ReadAsync("owner-a", plan.Id, token)).ShouldNotBeNull();
        saved.Version.ShouldBe(plan.Version + 1);
        return saved;
    }

    private sealed class ScenarioClock : TimeProvider
    {
        public DateOnly Today { get; set; } = HouseholdScenario.Start;
        public override DateTimeOffset GetUtcNow() => new(Today.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc));
    }
}
