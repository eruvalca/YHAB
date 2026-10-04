using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;
using YHAB.Features.Budgeting.Data;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.IntegrationTests;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class SelectiveOperationScaleTests(ITestOutputHelper output)
{
    [Fact]
    public async Task SelectiveAndFullLedgerOperationsRetainExactResultsAtEveryThresholdAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));
        var token = timeout.Token;
        foreach (var count in new[] { 10_000, 50_000, 100_000 })
        {
            var probe = new DatabaseProbe();
            await using var database = await BudgetDatabase.CreateAsync(token, interceptor: probe);
            var fixture = await LedgerScaleFixture.CreateAsync(database, count, 10, 50, 120, token);
            var version = 0L;
            async Task<PlanSnapshot> ApplyAsync(string name, PlanCommand command)
            {
                probe.Start();
                var result = (await database.Store.ExecuteAsync("owner-a", fixture.Id, command, token)).AsT0;
                output.WriteLine($"{count} / {name}: {probe.Stop()}.");
                result.Version.ShouldBe(++version);
                return result;
            }
            var category = fixture.Categories[0].Id;
            (await ApplyAsync("plan settings", new UpdatePlan(version, "Measured plan", "Scale validation"))).Name.ShouldBe("Measured plan");
            probe.Materialized.GetValueOrDefault(nameof(BudgetTransaction)).ShouldBe(0);
            var group = new GroupData(fixture.Categories[0].GroupId, "Measured group", 0);
            (await ApplyAsync("group edit", new SaveGroup(version, group))).Groups.Single().Name.ShouldBe(group.Name);
            probe.Materialized.GetValueOrDefault(nameof(BudgetTransaction)).ShouldBe(0);
            var changedCategory = fixture.Categories[0] with { Notes = "Measured category" };
            (await ApplyAsync("category edit", new SaveCategory(version, changedCategory))).Categories.Single(item => item.Id == category).Notes.ShouldBe(changedCategory.Notes);
            probe.Materialized.GetValueOrDefault(nameof(BudgetTransaction)).ShouldBe(0);
            var changedAccount = fixture.Accounts[0] with { Notes = "Measured account" };
            (await ApplyAsync("account edit", new SaveAccount(version, changedAccount))).Accounts.Single(item => item.Id == changedAccount.Id).Notes.ShouldBe(changedAccount.Notes);
            probe.Materialized.GetValueOrDefault(nameof(BudgetTransaction)).ShouldBe(0);
            var assigned = new AssignMoney(version, category, fixture.Month, 1000);
            (await ApplyAsync("assignment", assigned)).Allocations.Single(item => item.CategoryId == category && item.Month == fixture.Month).Amount.ShouldBe(1000);
            probe.Materialized.GetValueOrDefault(nameof(BudgetTransaction)).ShouldBe(0);
            probe.Materialized.GetValueOrDefault(nameof(BudgetAllocation)).ShouldBe(1);
            probe.Start();
            (await database.Store.ExecuteAsync("owner-a", fixture.Id, assigned, token)).AsT0.Version.ShouldBe(version);
            output.WriteLine($"{count} / receipt retry: {probe.Stop()}.");
            probe.Materialized.GetValueOrDefault(nameof(BudgetTransaction)).ShouldBe(0);
            probe.Materialized.GetValueOrDefault(nameof(BudgetAllocation)).ShouldBe(0);
            (await ApplyAsync("undo", new UndoChange(version))).Allocations.Single(item => item.CategoryId == category && item.Month == fixture.Month).Amount.ShouldBe(1);
            probe.Materialized.GetValueOrDefault(nameof(BudgetTransaction)).ShouldBe(0);
            probe.Materialized.GetValueOrDefault(nameof(BudgetAllocation)).ShouldBe(1);
            (await ApplyAsync("redo", new RedoChange(version))).Allocations.Single(item => item.CategoryId == category && item.Month == fixture.Month).Amount.ShouldBe(1000);
            probe.Materialized.GetValueOrDefault(nameof(BudgetTransaction)).ShouldBe(0);
            probe.Materialized.GetValueOrDefault(nameof(BudgetAllocation)).ShouldBe(1);
            // UI loads this month before funding. Measure subsequent edits against
            // its persisted opening rather than conflating cold replay with writes.
            await database.Queries.MonthAsync("owner-a", fixture.Id, fixture.Month, version, token);
            var moved = await ApplyAsync("move money", new MoveMoney(version, category, fixture.Categories[1].Id, fixture.Month, 50));
            probe.Materialized.GetValueOrDefault(nameof(BudgetTransaction)).ShouldBe(0);
            moved.Allocations.Single(item => item.CategoryId == category && item.Month == fixture.Month).Amount.ShouldBe(950);
            moved.Allocations.Single(item => item.CategoryId == fixture.Categories[1].Id && item.Month == fixture.Month).Amount.ShouldBe(51);
            var funded = await ApplyAsync("fund targets", new AutoAssign(version, fixture.Month));
            probe.Materialized.GetValueOrDefault(nameof(BudgetTransaction)).ShouldBeLessThan(count / 100);
            var fundedMonth = await database.Queries.MonthAsync("owner-a", fixture.Id, fixture.Month, version, token);
            fundedMonth.Categories.ShouldAllBe(item => item.Available == 2000);
            fundedMonth.ReadyToAssign.ShouldBe(fixture.Cash - 100_000);
            (await ApplyAsync("rename payee", new RenamePayee(version, "Market", "Corner store"))).Transactions.ShouldBeEmpty();
            probe.Materialized.GetValueOrDefault(nameof(BudgetTransaction)).ShouldBe(0);
            probe.Materialized.GetValueOrDefault(nameof(BudgetSplit)).ShouldBe(0);
            await using (var historyContext = await database.Factory.CreateDbContextAsync(token))
            {
                var history = await historyContext.Set<BudgetHistory>().Where(item => item.PlanId == fixture.Id).OrderByDescending(item => item.Position).FirstAsync(token);
                output.WriteLine($"{count} / payee history: {history.Before.Length + history.After.Length:N0} JSON characters.");
                history.Before.ShouldNotContain("accountId");
                history.After.ShouldNotContain("splits");
                using var names = JsonDocument.Parse(history.Before);
                names.RootElement.GetProperty("values").GetArrayLength().ShouldBe(count / 10 * 9);
            }
            await ApplyAsync("undo payee rename", new UndoChange(version));
            probe.Materialized.GetValueOrDefault(nameof(BudgetTransaction)).ShouldBe(0);
            await using (var verification = await database.Factory.CreateDbContextAsync(token))
            {
                (await verification.Set<BudgetTransaction>().CountAsync(item => item.PlanId == fixture.Id && item.Payee == "Market", token)).ShouldBe(count / 10 * 9);
            }
            await ApplyAsync("redo payee rename", new RedoChange(version));
            probe.Materialized.GetValueOrDefault(nameof(BudgetTransaction)).ShouldBe(0);
            var renamed = (await database.Store.ReadAsync("owner-a", fixture.Id, token)).ShouldNotBeNull();
            renamed.Transactions.Count(item => string.Equals(item.Payee, "Corner store", StringComparison.Ordinal)).ShouldBe(count / 10 * 9);
            var selected = renamed.Transactions.OrderBy(item => item.Sequence).Take(50).ToArray();
            var cleared = await ApplyAsync("clear 50", new UpdateTransactionStates(version, selected.Select(item => item.Id).ToArray(), ClearingState.Cleared, false, null));
            cleared.Transactions.Count(item => item.State == ClearingState.Cleared).ShouldBe(50);
            probe.Materialized.GetValueOrDefault(nameof(BudgetTransaction)).ShouldBe(50);
            var edited = selected[0] with { State = ClearingState.Cleared, Payee = "Edited merchant" };
            (await ApplyAsync("edit one", new SaveTransaction(version, edited))).Transactions.Single().Payee.ShouldBe("Edited merchant");
            probe.Materialized.GetValueOrDefault(nameof(BudgetTransaction)).ShouldBe(1);
            (await ApplyAsync("delete 50", new DeleteTransactions(version, selected.Select(item => item.Id).ToArray()))).Transactions.ShouldBeEmpty();
            probe.Materialized.GetValueOrDefault(nameof(BudgetTransaction)).ShouldBe(50);
            var reconciled = await ApplyAsync("reconcile account", new ReconcileAccount(version, fixture.Accounts[0].Id, funded.Today, 1_000_001, true));
            probe.Materialized.GetValueOrDefault(nameof(BudgetTransaction)).ShouldBe(0);
            reconciled.Transactions.Count(item => item.State == ClearingState.Reconciled).ShouldBe(1);
            var merged = await ApplyAsync("merge category", new RemoveCategory(version, fixture.Categories[1].Id, fixture.Categories[2].Id));
            merged.Categories.Count.ShouldBe(49);
            merged.Transactions.SelectMany(item => item.Splits).ShouldAllBe(item => item.CategoryId != fixture.Categories[1].Id);
            var view = await database.Queries.ViewAsync("owner-a", fixture.Id, token);
            view.Balances.Sum(item => item.Working).ShouldBe(fixture.Cash - selected.Sum(item => item.Amount) + 1);
            await using var context = await database.Factory.CreateDbContextAsync(token);
            (await context.Set<BudgetTransaction>().CountAsync(item => item.PlanId == fixture.Id, token)).ShouldBe(count - 49);
            (await context.Set<BudgetReceipt>().CountAsync(item => item.PlanId == fixture.Id, token)).ShouldBe((int)version);
            probe.Start();
            var reports = await database.Queries.ReportsAsync("owner-a", fixture.Id, fixture.Month.AddMonths(-23), funded.Today, token);
            output.WriteLine($"{count} / two-year report: {probe.Stop()}; response {JsonSerializer.SerializeToUtf8Bytes(reports).Length} bytes.");
            probe.Materialized.GetValueOrDefault(nameof(BudgetTransaction)).ShouldBe(0);
            probe.Materialized.GetValueOrDefault(nameof(BudgetSplit)).ShouldBe(0);
            probe.Materialized.GetValueOrDefault(nameof(BudgetAllocation)).ShouldBe(0);
            reports.Months.Count.ShouldBe(24);
            reports.Months[^1].NetWorth.ShouldBe(view.Balances.Sum(item => item.Working));
            var firstDue = funded.Today.AddDays(-364);
            var template = new TransactionData(Guid.Empty, fixture.Accounts[1].Id, firstDue, "Daily fee", "", -1, null,
                ClearingState.Uncleared, ClearingState.Uncleared, false, "", [new(Guid.Empty, fixture.Categories[^1].Id, -1, "")], RepeatFrequency.Daily, firstDue);
            var created = await ApplyAsync("create recurring template", new SaveTransaction(version, template));
            var templateId = created.Transactions.Single(item => item.Repeat == RepeatFrequency.Daily).Id;
            created.Transactions.Count(item => item.SourceTemplateId == templateId).ShouldBe(128);
            for (var batch = 2; batch <= 3; batch++)
            {
                var posted = await ApplyAsync($"recurring batch {batch}", new PostRecurring(version, funded.Today));
                posted.Transactions.Count(item => item.SourceTemplateId == templateId).ShouldBe(Math.Min(128, 365 - (batch - 1) * 128));
                probe.Materialized.GetValueOrDefault(nameof(BudgetTransaction)).ShouldBe(1);
            }
            var final = (await database.Store.ReadAsync("owner-a", fixture.Id, token)).ShouldNotBeNull();
            final.Transactions.Single(item => item.Id == templateId).Date.ShouldBe(funded.Today.AddDays(1));
            final.Transactions.Where(item => item.SourceTemplateId == templateId).Select(item => item.ScheduledDate).Order()
                .ShouldBe(Enumerable.Range(0, 365).Select(day => (DateOnly?)firstDue.AddDays(day)).ToArray());
            final.Accounts.Sum(account => BudgetFacts.Balance(final, account, final.Today).Working).ShouldBe(view.Balances.Sum(item => item.Working) - 365);
            (await context.Set<BudgetReceipt>().CountAsync(item => item.PlanId == fixture.Id, token)).ShouldBe((int)version);
        }
    }

    [Fact]
    public async Task WideCatalogsAndLongAllocationHistoriesRetainExactBalancesAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));
        var token = timeout.Token;
        foreach (var size in new[] { (Accounts: 10, Categories: 50, Months: 60), (Accounts: 50, Categories: 200, Months: 120), (Accounts: 100, Categories: 500, Months: 240) })
        {
            var probe = new DatabaseProbe();
            await using var database = await BudgetDatabase.CreateAsync(token, interceptor: probe);
            var fixture = await LedgerScaleFixture.CreateAsync(database, 10_000, size.Accounts, size.Categories, size.Months, token);
            var label = $"{size.Accounts} accounts / {size.Categories} categories / {size.Months} months";
            probe.Start();
            var view = await database.Queries.ViewAsync("owner-a", fixture.Id, token);
            output.WriteLine($"{label} / catalog: {probe.Stop()}; response {JsonSerializer.SerializeToUtf8Bytes(view).Length} bytes.");
            view.Balances.Sum(item => item.Working).ShouldBe(fixture.Cash);
            view.Catalog.Allocations.ShouldBeEmpty();
            probe.Materialized.GetValueOrDefault(nameof(BudgetAllocation)).ShouldBe(0);
            view.Catalog.Transactions.ShouldBeEmpty();
            probe.Start();
            var cold = await database.Queries.MonthAsync("owner-a", fixture.Id, fixture.Month, 0, token);
            output.WriteLine($"{label} / cold month: {probe.Stop()}.");
            cold.Assigned.ShouldBe(size.Categories);
            (cold.ReadyToAssign + cold.Available).ShouldBe(fixture.Cash);
            probe.Start();
            (await database.Store.ExecuteAsync("owner-a", fixture.Id, new AssignMoney(0, fixture.Categories[0].Id, fixture.Month, 2), token)).IsT0.ShouldBeTrue();
            output.WriteLine($"{label} / assignment: {probe.Stop()}.");
            probe.Materialized.GetValueOrDefault(nameof(BudgetTransaction)).ShouldBe(0);
            probe.Materialized.GetValueOrDefault(nameof(BudgetAllocation)).ShouldBe(1);
            probe.Start();
            var warm = await database.Queries.MonthAsync("owner-a", fixture.Id, fixture.Month, 1, token);
            output.WriteLine($"{label} / resumed month: {probe.Stop()}.");
            probe.Materialized.GetValueOrDefault(nameof(BudgetAllocation)).ShouldBe(size.Categories);
            probe.Materialized.GetValueOrDefault(nameof(BudgetCheckpoint)).ShouldBe(1);
            warm.Assigned.ShouldBe(size.Categories + 1);
            (warm.ReadyToAssign + warm.Available).ShouldBe(fixture.Cash);
            await using var context = await database.Factory.CreateDbContextAsync(token);
            var allocations = await context.Set<BudgetAllocation>().Where(item => item.PlanId == fixture.Id).ToArrayAsync(token);
            allocations.Length.ShouldBe(size.Categories * size.Months);
            allocations.Single(item => item.CategoryId == fixture.Categories[0].Id && item.Month == fixture.Month).Amount.ShouldBe(2);
            allocations.Where(item => item.CategoryId != fixture.Categories[0].Id || item.Month != fixture.Month).ShouldAllBe(item => item.Amount == 1);
            var checkpoints = await context.Set<BudgetCheckpoint>().Where(item => item.PlanId == fixture.Id).Select(item => item.State).ToArrayAsync(token);
            checkpoints.Length.ShouldBe(size.Months + 1);
            output.WriteLine($"{label} / checkpoints: {checkpoints.Sum(item => item.Length):N0} JSON characters in {checkpoints.Length} openings.");
        }
    }
}
