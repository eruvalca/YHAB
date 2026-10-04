using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;
using YHAB.Features.Budgeting.Data;
using YHAB.Features.Budgeting.Models;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.IntegrationTests;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class RecurringReadSetTests(ITestOutputHelper output)
{
    [Fact]
    public async Task LongRunningTemplatesReadOnlyCandidateIdentitiesAndRespectPlanScopeAsync()
    {
        var token = TestContext.Current.CancellationToken;
        var probe = new DatabaseProbe();
        await using var database = await BudgetDatabase.CreateAsync(token, interceptor: probe);
        var id = await database.Store.CreateAsync("owner-a", new("Long recurrence", StarterCategories: false), token);
        var foreignId = await database.Store.CreateAsync("owner-b", new("Foreign recurrence", StarterCategories: false), token);
        var today = (await database.Store.ReadAsync("owner-a", id, token)).ShouldNotBeNull().Today;
        var start = today.AddDays(-1000);
        var account = new AccountData(Guid.NewGuid(), "Tracking", AccountKind.Asset, 10_000, start, false, "");
        var template = new TransactionData(Guid.NewGuid(), account.Id, today.AddDays(-2), "Daily", "", -1, null,
            ClearingState.Uncleared, ClearingState.Uncleared, false, "", [], RepeatFrequency.Daily, start, 998, Sequence: 1);
        await SeedAsync(id, 998);
        await SeedAsync(foreignId, 1001);

        probe.Start();
        var posted = (await database.Store.ExecuteAsync("owner-a", id, new PostRecurring(0, today), token)).AsT0;
        output.WriteLine($"Post after 998 existing occurrences: {probe.Stop()}");
        probe.Materialized.GetValueOrDefault(nameof(BudgetTransaction)).ShouldBe(1);
        posted.Transactions.Where(item => item.SourceTemplateId == template.Id).Select(item => item.ScheduledDate).Order()
            .ShouldBe([today.AddDays(-2), today.AddDays(-1), today]);
        var full = (await database.Store.ReadAsync("owner-a", id, token)).ShouldNotBeNull();
        var first = full.Transactions.Single(item => item.ScheduledDate == start);
        (await database.Store.ExecuteAsync("owner-a", id, new SaveTransaction(1, first with { Date = today, Memo = "Moved actual date" }), token)).IsT0.ShouldBeTrue();

        probe.Start();
        var reset = (await database.Store.ExecuteAsync("owner-a", id,
            new SaveTransaction(2, posted.Transactions.Single(item => item.Id == template.Id) with { Date = start }), token)).AsT0;
        output.WriteLine($"Rewind across 1001 existing occurrences: {probe.Stop()}");
        probe.Materialized.GetValueOrDefault(nameof(BudgetTransaction)).ShouldBe(1);
        probe.Materialized.GetValueOrDefault(nameof(RecurringOccurrence)).ShouldBe(128);
        reset.Transactions.Single().Date.ShouldBe(start.AddDays(128));
        (await database.Store.ExecuteAsync("owner-a", id, new UndoChange(3), token)).IsT0.ShouldBeTrue();
        var undone = (await database.Store.ReadAsync("owner-a", id, token)).ShouldNotBeNull();
        undone.Transactions.Single(item => item.Id == template.Id).Date.ShouldBe(today.AddDays(1));
        (await database.Store.ExecuteAsync("owner-a", id, new RedoChange(4), token)).IsT0.ShouldBeTrue();
        probe.Start();
        var next = (await database.Store.ExecuteAsync("owner-a", id, new PostRecurring(5, today), token)).AsT0;
        output.WriteLine($"Next duplicate-only batch: {probe.Stop()}");
        probe.Materialized.GetValueOrDefault(nameof(BudgetTransaction)).ShouldBe(1);
        probe.Materialized.GetValueOrDefault(nameof(RecurringOccurrence)).ShouldBe(128);
        next.Transactions.Single().Date.ShouldBe(start.AddDays(256));
        full = (await database.Store.ReadAsync("owner-a", id, token)).ShouldNotBeNull();
        full.Transactions.Count.ShouldBe(1002);
        full.Transactions.Where(item => item.SourceTemplateId == template.Id).Select(item => item.ScheduledDate).Order()
            .ShouldBe(Enumerable.Range(0, 1001).Select(day => (DateOnly?)start.AddDays(day)).ToArray());
        full.Transactions.Single(item => item.Id == first.Id).Date.ShouldBe(today);
        full.Transactions.Single(item => item.Id == first.Id).Memo.ShouldBe("Moved actual date");
        full.Transactions.Select(item => item.Sequence).Distinct().Count().ShouldBe(1002);
        BudgetFacts.Balance(full, account, today).Working.ShouldBe(8999);
        var foreign = (await database.Store.ReadAsync("owner-b", foreignId, token)).ShouldNotBeNull();
        foreign.Version.ShouldBe(0);
        foreign.Transactions.Count.ShouldBe(1002);

        async Task SeedAsync(Guid planId, int count)
        {
            await using var context = await database.Factory.CreateDbContextAsync(token);
            var plan = await context.Set<BudgetPlan>().SingleAsync(item => item.Id == planId, token);
            plan.NextSequence = 1002;
            var occurrences = Enumerable.Range(0, count).Select(day => template with
            {
                Id = Guid.NewGuid(),
                Date = start.AddDays(day),
                Repeat = RepeatFrequency.None,
                SourceTemplateId = template.Id,
                ScheduledDate = start.AddDays(day),
                Sequence = day + 2,
            });
            var snapshot = new PlanSnapshot(planId, plan.Name, "", today, 0, [account], [], [], [], [template, .. occurrences], false, false, []);
            BudgetSnapshotMapping.Apply(context, snapshot);
            await context.SaveChangesAsync(token);
        }
    }
}
