using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;
using YHAB.Features.Budgeting.Data;
using YHAB.Features.Budgeting.Services;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.IntegrationTests;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class CheckpointPublicationTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ColdReplayWithoutEntityCountingRetainsExactBalancesAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var token = timeout.Token;
        // Run sequentially: combining each dimension's maximum is a distinct
        // measurement, and parallel fixtures would contaminate allocation deltas.
        foreach (var shape in new[] { (Entries: 100_000, Accounts: 2, Categories: 8, Months: 24), (Entries: 10_000, Accounts: 100, Categories: 500, Months: 240), (Entries: 100_000, Accounts: 100, Categories: 500, Months: 240) })
        {
            output.WriteLine($"Replay shape: {shape.Entries} transactions, {shape.Accounts} accounts, {shape.Categories} categories, {shape.Months} months.");
            await VerifyColdReplayAsync(shape.Entries, shape.Accounts, shape.Categories, shape.Months, token);
        }
    }

    private async Task VerifyColdReplayAsync(int entries, int accounts, int categories, int months, CancellationToken token)
    {
        var probe = new DatabaseProbe();
        await using var database = await BudgetDatabase.CreateAsync(token, interceptor: probe.CommandsOnly());
        var fixture = await LedgerScaleFixture.CreateAsync(database, entries, accounts, categories, months, token);
        var full = (await database.Store.ReadAsync("owner-a", fixture.Id, token)).ShouldNotBeNull();
        await using var context = await database.Factory.CreateDbContextAsync(token);
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        var plan = await context.Set<BudgetPlan>().SingleAsync(item => item.Id == fixture.Id, token);
        var catalog = (await BudgetSnapshotMapping.LoadAsync(context, plan, token, includeLedger: false, includeAllocations: false)) with { Today = full.Today };
        probe.Start();
        var replay = await BudgetMonthReplay.ReadAsync(context, catalog, fixture.Month, token);
        output.WriteLine($"Cold replay without entity counting: {probe.Stop()}.");
        probe.Commands.Count.ShouldBeGreaterThan(0);
        probe.Materialized.ShouldBeEmpty(); // Disabled instrumentation, not zero database rows.
        var expected = BudgetCalculator.Calculate(full, fixture.Month, full.Today);
        (replay.Budget with { Categories = expected.Categories }).ShouldBe(expected);
        replay.Budget.Categories.ShouldBe(expected.Categories);
        (replay.Budget.ReadyToAssign + replay.Budget.Available).ShouldBe(fixture.Cash);
        replay.States.Select(item => item.Month).ShouldBe(Enumerable.Range(0, fixture.Months + 1).Select(index => fixture.Start.AddMonths(index)));
        await BudgetCheckpoints.SaveAsync(database.Factory, "owner-a", fixture.Id, full.Version, replay.States, token);
        probe.Start();
        var warm = await database.Queries.MonthAsync("owner-a", fixture.Id, fixture.Month, full.Version, token);
        output.WriteLine($"Warm month without entity counting: {probe.Stop()}.");
        (warm with { Categories = expected.Categories }).ShouldBe(expected);
        warm.Categories.ShouldBe(expected.Categories);
        (warm.ReadyToAssign + warm.Available).ShouldBe(fixture.Cash);
    }

    [Fact]
    public async Task CurrentMonthEditsBetweenReplayAndPublicationPreserveExactBalancesAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var token = timeout.Token;
        var probe = new DatabaseProbe();
        await using var database = await BudgetDatabase.CreateAsync(token, interceptor: probe);
        var fixture = await LedgerScaleFixture.CreateAsync(database, 100_000, 2, 8, 24, token);
        var full = (await database.Store.ReadAsync("owner-a", fixture.Id, token)).ShouldNotBeNull();
        var category = fixture.Categories[0].Id;
        // Force the read/edit/publish order without timing sleeps or parallel
        // probes. Each replay uses a consistent pre-edit input, as MonthAsync does.
        for (var cycle = 0; cycle < 3; cycle++)
        {
            await using var context = await database.Factory.CreateDbContextAsync(token);
            context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            var plan = await context.Set<BudgetPlan>().SingleAsync(item => item.Id == fixture.Id, token);
            var catalog = (await BudgetSnapshotMapping.LoadAsync(context, plan, token, includeLedger: false, includeAllocations: false)) with { Today = full.Today };
            probe.Start();
            var replay = await BudgetMonthReplay.ReadAsync(context, catalog, fixture.Month, token);
            output.WriteLine($"Interleaved replay {cycle + 1}: {probe.Stop()}.");
            probe.Materialized.GetValueOrDefault(nameof(BudgetTransaction)).ShouldBe(cycle == 0 ? fixture.Entries : full.Transactions.Count(item => item.Date >= fixture.Month));
            var expected = BudgetCalculator.Calculate(full, fixture.Month, full.Today);
            (replay.Budget with { Categories = expected.Categories }).ShouldBe(expected);
            replay.Budget.Categories.ShouldBe(expected.Categories);
            (replay.Budget.ReadyToAssign + replay.Budget.Available).ShouldBe(fixture.Cash);

            var amount = 100 + cycle;
            var saved = (await database.Store.ExecuteAsync("owner-a", fixture.Id, new AssignMoney(full.Version, category, fixture.Month, amount), token)).AsT0;
            saved.Version.ShouldBe(full.Version + 1);
            await BudgetCheckpoints.SaveAsync(database.Factory, "owner-a", fixture.Id, full.Version, replay.States, token);
            var openings = await context.Set<BudgetCheckpoint>().Where(item => item.PlanId == fixture.Id).Select(item => item.Month).ToArrayAsync(token);
            openings.Order().ShouldBe(Enumerable.Range(0, fixture.Months).Select(index => fixture.Start.AddMonths(index)));
            output.WriteLine($"Interleaved publication {cycle + 1}: {openings.Length} stored openings.");
            full = full with
            {
                Version = saved.Version,
                Allocations = full.Allocations.Select(item => item.CategoryId == category && item.Month == fixture.Month ? item with { Amount = amount } : item).ToArray(),
            };
        }
        // Once a publication has no competing edit, its openings must support a
        // subsequent current-month edit without replaying historical transactions.
        var final = await database.Queries.MonthAsync("owner-a", fixture.Id, fixture.Month, full.Version, token);
        final.Categories.Single(item => item.Category.Id == category).Assigned.ShouldBe(102);
        (final.ReadyToAssign + final.Available).ShouldBe(fixture.Cash);
        var changed = (await database.Store.ExecuteAsync("owner-a", fixture.Id, new AssignMoney(full.Version, category, fixture.Month, 200), token)).AsT0;
        probe.Start();
        var warm = await database.Queries.MonthAsync("owner-a", fixture.Id, fixture.Month, changed.Version, token);
        output.WriteLine($"Uncontended warm replay: {probe.Stop()}.");
        probe.Materialized.GetValueOrDefault(nameof(BudgetTransaction)).ShouldBe(full.Transactions.Count(item => item.Date >= fixture.Month));
        warm.Categories.Single(item => item.Category.Id == category).Assigned.ShouldBe(200);
        (warm.ReadyToAssign + warm.Available).ShouldBe(fixture.Cash);
    }

    [Fact]
    public async Task CompactedBoundariesProtectOldRevisionsAcrossBackdatingUndoAndRedoAsync()
    {
        var token = TestContext.Current.CancellationToken;
        await using var database = await BudgetDatabase.CreateAsync(token);
        var fixture = await LedgerScaleFixture.CreateAsync(database, 120, 2, 2, 4, token);
        var original = (await database.Store.ReadAsync("owner-a", fixture.Id, token)).ShouldNotBeNull();
        var oldStates = await ReplayAsync(database, original, fixture.Month, token);
        var category = fixture.Categories[0].Id;
        var august = fixture.Start.AddMonths(1);
        (await database.Store.ExecuteAsync("owner-a", fixture.Id, new AssignMoney(0, category, august, 30), token)).IsT0.ShouldBeTrue();
        var first = (await database.Store.ReadAsync("owner-a", fixture.Id, token)).ShouldNotBeNull();
        var firstStates = await ReplayAsync(database, first, fixture.Month, token);
        (await database.Store.ExecuteAsync("owner-a", fixture.Id, new AssignMoney(1, category, fixture.Month, 40), token)).IsT0.ShouldBeTrue();
        (await database.Store.ExecuteAsync("owner-a", fixture.Id, new UpdatePlan(2, "Metadata only", ""), token)).IsT0.ShouldBeTrue();
        await using var context = await database.Factory.CreateDbContextAsync(token);
        var boundaries = await context.Set<BudgetCheckpointInvalidation>().OrderBy(item => item.Version).ToArrayAsync(token);
        boundaries.Select(item => (item.Version, item.FirstInvalidMonth)).ShouldBe([(1L, august.AddMonths(1)), (2L, fixture.Month.AddMonths(1))]);

        await BudgetCheckpoints.SaveAsync(database.Factory, "owner-a", fixture.Id, 0, oldStates, token);
        (await context.Set<BudgetCheckpoint>().OrderBy(item => item.Month).Select(item => item.Month).ToArrayAsync(token))
            .ShouldBe([fixture.Start, august]);
        await BudgetCheckpoints.SaveAsync(database.Factory, "owner-a", fixture.Id, 1, firstStates, token);
        (await context.Set<BudgetCheckpoint>().CountAsync(token)).ShouldBe(4);
        await VerifyCurrentAsync(database, fixture, token);

        (await database.Store.ExecuteAsync("owner-a", fixture.Id, new AssignMoney(3, category, fixture.Start, 20), token)).IsT0.ShouldBeTrue();
        // A newer earlier invalidation replaces both old boundaries. It must
        // still protect calculations from before either removed revision.
        var compacted = await context.Set<BudgetCheckpointInvalidation>().AsNoTracking().SingleAsync(token);
        compacted.Version.ShouldBe(4);
        compacted.FirstInvalidMonth.ShouldBe(august);
        await BudgetCheckpoints.SaveAsync(database.Factory, "owner-a", fixture.Id, 0, oldStates, token);
        await BudgetCheckpoints.SaveAsync(database.Factory, "owner-a", fixture.Id, 1, firstStates, token);
        (await context.Set<BudgetCheckpoint>().Select(item => item.Month).ToArrayAsync(token)).ShouldBe([fixture.Start]);
        await VerifyCurrentAsync(database, fixture, token);

        foreach (var command in new PlanCommand[] { new UndoChange(4), new RedoChange(5) })
        {
            (await database.Store.ExecuteAsync("owner-a", fixture.Id, command, token)).IsT0.ShouldBeTrue();
            var boundary = await context.Set<BudgetCheckpointInvalidation>().AsNoTracking().SingleAsync(token);
            boundary.Version.ShouldBe(command.Version + 1);
            boundary.FirstInvalidMonth.ShouldBe(august);
            await BudgetCheckpoints.SaveAsync(database.Factory, "owner-a", fixture.Id, 1, firstStates, token);
            (await context.Set<BudgetCheckpoint>().CountAsync(token)).ShouldBe(1);
            await VerifyCurrentAsync(database, fixture, token);
        }
    }

    [Fact]
    public async Task ForeignBoundariesAndFutureRevisionsCannotChangeOwnedPublicationAsync()
    {
        var token = TestContext.Current.CancellationToken;
        await using var database = await BudgetDatabase.CreateAsync(token);
        var fixture = await LedgerScaleFixture.CreateAsync(database, 120, 2, 2, 4, token);
        var plan = (await database.Store.ReadAsync("owner-a", fixture.Id, token)).ShouldNotBeNull();
        var states = await ReplayAsync(database, plan, fixture.Month, token);
        var other = await database.Store.CreateAsync("owner-b", new("Foreign", StarterCategories: false), token);
        // A foreign reset must neither restrict this plan nor disappear during
        // this plan's compaction, even with overlapping account identifiers.
        (await database.Store.ExecuteAsync("owner-b", other, new SaveAccount(0, fixture.Accounts[0]), token)).IsT0.ShouldBeTrue();
        (await database.Store.ExecuteAsync("owner-a", fixture.Id, new UpdatePlan(0, "Renamed", ""), token)).IsT0.ShouldBeTrue();
        await BudgetCheckpoints.SaveAsync(database.Factory, "owner-b", fixture.Id, 0, states, token);
        await BudgetCheckpoints.SaveAsync(database.Factory, "owner-a", fixture.Id, 2, states, token);
        await using var context = await database.Factory.CreateDbContextAsync(token);
        (await context.Set<BudgetCheckpoint>().CountAsync(token)).ShouldBe(0);
        // Metadata changed the revision but none of the calculated openings.
        await BudgetCheckpoints.SaveAsync(database.Factory, "owner-a", fixture.Id, 0, states, token);
        (await context.Set<BudgetCheckpoint>().CountAsync(token)).ShouldBe(5);
        (await database.Queries.RevisionAsync("owner-a", fixture.Id, token)).ShouldBe(1);
        (await Should.ThrowAsync<BudgetRequestException>(() => database.Queries.MonthAsync("owner-a", fixture.Id, fixture.Month, 0, token))).Status.ShouldBe(409);

        var changed = fixture.Accounts[0] with { OpeningBalance = fixture.Accounts[0].OpeningBalance + 1 };
        (await database.Store.ExecuteAsync("owner-a", fixture.Id, new SaveAccount(1, changed), token)).IsT0.ShouldBeTrue();
        await BudgetCheckpoints.SaveAsync(database.Factory, "owner-a", fixture.Id, 0, states, token);
        (await context.Set<BudgetCheckpoint>().CountAsync(token)).ShouldBe(0);
        var boundaries = await context.Set<BudgetCheckpointInvalidation>().ToArrayAsync(token);
        boundaries.Length.ShouldBe(2);
        boundaries.Single(item => item.PlanId == fixture.Id).FirstInvalidMonth.ShouldBe(DateOnly.MinValue);
        boundaries.Single(item => item.PlanId == other).Version.ShouldBe(1);
        await VerifyCurrentAsync(database, fixture, token);
        await context.Set<BudgetPlan>().Where(item => item.Id == fixture.Id).ExecuteDeleteAsync(token);
        (await context.Set<BudgetCheckpoint>().CountAsync(token)).ShouldBe(0);
        (await context.Set<BudgetCheckpointInvalidation>().Select(item => item.PlanId).ToArrayAsync(token)).ShouldBe([other]);
    }

    private static async Task<IReadOnlyList<BudgetMonthState>> ReplayAsync(BudgetDatabase database, PlanSnapshot plan, DateOnly month, CancellationToken token)
    {
        await using var context = await database.Factory.CreateDbContextAsync(token);
        return (await BudgetMonthReplay.ReadAsync(context, plan, month, token)).States;
    }

    private static async Task VerifyCurrentAsync(BudgetDatabase database, LedgerScaleFixture fixture, CancellationToken token)
    {
        var full = (await database.Store.ReadAsync("owner-a", fixture.Id, token)).ShouldNotBeNull();
        var expected = BudgetCalculator.Calculate(full, fixture.Month, full.Today);
        var actual = await database.Queries.MonthAsync("owner-a", fixture.Id, fixture.Month, full.Version, token);
        (actual with { Categories = expected.Categories }).ShouldBe(expected);
        actual.Categories.ShouldBe(expected.Categories);
    }
}
