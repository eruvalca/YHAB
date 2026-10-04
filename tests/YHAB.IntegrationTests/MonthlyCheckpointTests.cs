using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;
using YHAB.Features.Budgeting.Data;
using YHAB.Features.Budgeting.Services;
using YHAB.SharedKernel.Budgeting;
using YHAB.Tests.Scenarios;

namespace YHAB.IntegrationTests;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class MonthlyCheckpointTests
{
    [Theory]
    [InlineData(TargetKind.Refill, 150, 2)]
    [InlineData(TargetKind.SetAside, 165, 1)]
    [InlineData(TargetKind.Balance, 170, 1)]
    public async Task TargetAggregatesAndMissingPeriodOpeningsPreserveExactMonthlyFundingAsync(TargetKind kind, decimal needed, int checkpointReads)
    {
        var token = TestContext.Current.CancellationToken;
        var probe = new DatabaseProbe();
        await using var database = await BudgetDatabase.CreateAsync(token, interceptor: probe);
        var id = await database.Store.CreateAsync("owner-a", new("Target periods", StarterCategories: false), token);
        var empty = (await database.Store.ReadAsync("owner-a", id, token)).ShouldNotBeNull();
        var january = new DateOnly(2026, 1, 1);
        var march = january.AddMonths(2);
        var account = new AccountData(Guid.CreateVersion7(), "Checking", AccountKind.Checking, 1000, january, false, "");
        var group = new GroupData(Guid.CreateVersion7(), "Goals", 0);
        var category = new CategoryData(Guid.CreateVersion7(), group.Id, "Annual bill", "", 0, false, null,
            new(kind, TargetCadence.Custom, 500, january.AddMonths(1), january.AddMonths(3)));
        var plan = empty with
        {
            Accounts = [account],
            Groups = [group],
            Categories = [category],
            Allocations = [new(category.Id, january, 30), new(category.Id, january.AddMonths(1), 110), new(category.Id, march, 30), new(category.Id, january.AddMonths(4), 80)],
            Transactions = [new(Guid.CreateVersion7(), account.Id, january.AddMonths(1).AddDays(2), "Bill", "", -40, null,
                ClearingState.Uncleared, ClearingState.Uncleared, false, "", [new(Guid.CreateVersion7(), category.Id, -40, "")])],
        };
        await using var context = await database.Factory.CreateDbContextAsync(token);
        BudgetSnapshotMapping.Apply(context, plan);
        await context.SaveChangesAsync(token);
        await using (var foreign = await database.Factory.CreateDbContextAsync(token))
        {
            var otherId = Guid.CreateVersion7();
            foreign.Add(new BudgetPlan { Id = otherId, OwnerId = "owner-b", Name = "Overlapping identifiers", CreatedOn = january });
            // Composite keys deliberately overlap across owners. Aggregate and
            // history queries must never include another plan's matching keys.
            BudgetSnapshotMapping.Apply(foreign, plan with
            {
                Id = otherId,
                Allocations = plan.Allocations.Select(item => item with { Amount = item.Amount * 10 }).ToArray(),
                Transactions = [],
            });
            await foreign.SaveChangesAsync(token);
        }
        var cold = await database.Queries.MonthAsync("owner-a", id, march, 0, token);
        cold.ReadyToAssign.ShouldBe(750);
        cold.Categories.Single().Available.ShouldBe(130);
        cold.Categories.Single().TargetNeeded.ShouldBe(needed);
        (await database.Store.ExecuteAsync("owner-a", id, new AssignMoney(0, category.Id, march, 40), token)).IsT0.ShouldBeTrue();
        probe.Start();
        var warm = await database.Queries.MonthAsync("owner-a", id, march, 1, token);
        probe.Stop();
        probe.Materialized.GetValueOrDefault(nameof(BudgetAllocation)).ShouldBe(1);
        probe.Materialized.GetValueOrDefault(nameof(BudgetCheckpoint)).ShouldBe(checkpointReads);
        probe.Materialized.GetValueOrDefault(nameof(BudgetTransaction)).ShouldBe(0);
        warm.ReadyToAssign.ShouldBe(740);
        warm.Categories.Single().TargetNeeded.ShouldBe(needed - 10);
        // Retain the latest opening but remove the dated refill's required start.
        // A revision change avoids a cached response without changing any money.
        await context.Set<BudgetCheckpoint>().Where(item => item.PlanId == id && item.Month == january.AddMonths(1)).ExecuteDeleteAsync(token);
        await context.Set<BudgetPlan>().Where(item => item.Id == id).ExecuteUpdateAsync(update => update.SetProperty(item => item.Version, 2), token);
        var repaired = await database.Queries.MonthAsync("owner-a", id, march, 2, token);
        JsonSerializer.Serialize(repaired).ShouldBe(JsonSerializer.Serialize(warm));
        (await database.Store.ExecuteAsync("owner-a", id, new AssignMoney(2, category.Id, january.AddMonths(4), 90), token)).IsT0.ShouldBeTrue();
        var future = await database.Queries.MonthAsync("owner-a", id, march, 3, token);
        future.ReadyToAssign.ShouldBe(730);
        future.AssignedInFuture.ShouldBe(90);
        future.Categories.Single().TargetNeeded.ShouldBe(needed - 10);
        var undone = (await database.Store.ExecuteAsync("owner-a", id, new UndoChange(3), token)).AsT0;
        undone.Allocations.Single().Amount.ShouldBe(80);
        (await database.Queries.MonthAsync("owner-a", id, march, 4, token)).ReadyToAssign.ShouldBe(740);
    }

    [Fact]
    public async Task MultiYearReplayAndVariedRepeatingTargetsAgreeAfterBackdatedAssignmentsAsync()
    {
        var token = TestContext.Current.CancellationToken;
        await using var database = await BudgetDatabase.CreateAsync(token);
        var fixture = await LedgerScaleFixture.CreateAsync(database, 1000, 3, 12, 36, token);
        await using var context = await database.Factory.CreateDbContextAsync(token);
        var categories = await context.Set<BudgetCategory>().Where(item => item.PlanId == fixture.Id).OrderBy(item => item.SortOrder).ToArrayAsync(token);
        for (var index = 0; index < categories.Length; index++)
        {
            var category = categories[index];
            category.TargetKind = index % 2 == 0 ? TargetKind.Refill : TargetKind.SetAside;
            category.TargetCadence = index < 6 ? TargetCadence.Yearly : TargetCadence.Custom;
            category.TargetStartMonth = fixture.Start.AddMonths(index - 3);
            category.TargetDueDate = fixture.Start.AddMonths(index + 8).AddDays(27);
            category.TargetRepeatMonths = index < 6 ? 0 : 3;
            category.TargetAmount = 2400;
        }
        await context.SaveChangesAsync(token);
        var plan = (await database.Store.ReadAsync("owner-a", fixture.Id, token)).ShouldNotBeNull();
        await VerifyMonthAsync(database, plan, fixture.Month, token);
        // This invalidates a middle segment, exercising both a saved opening and
        // year-sized replay windows, including different target cycle boundaries.
        plan = (await database.Store.ExecuteAsync("owner-a", plan.Id, new AssignMoney(0, categories[0].Id, fixture.Start.AddMonths(13), 73), token)).AsT0;
        await VerifyMonthAsync(database, plan, fixture.Month, token);
        plan = (await database.Store.ExecuteAsync("owner-a", plan.Id, new AssignMoney(1, categories[1].Id, fixture.Month, -5, true), token)).AsT0;
        await VerifyMonthAsync(database, plan, fixture.Month, token);
    }

    [Fact]
    public async Task CheckpointsSurviveCurrentEditsAndRebuildBackdatedUndoAndCatalogChangesAsync()
    {
        var token = TestContext.Current.CancellationToken;
        await using var database = await BudgetDatabase.CreateAsync(token);
        var plan = await SeedAsync(database, token);
        var march = new DateOnly(2026, 3, 1);
        await VerifyMonthAsync(database, plan, march, token);
        await using var context = await database.Factory.CreateDbContextAsync(token);
        (await context.Set<BudgetCheckpoint>().CountAsync(token)).ShouldBe(4);
        var january = await context.Set<BudgetCheckpoint>().AsNoTracking().SingleAsync(item => item.Month == new DateOnly(2026, 1, 1), token);
        var category = plan.Categories[0];
        plan = (await database.Store.ExecuteAsync("owner-a", plan.Id, new AssignMoney(plan.Version, category.Id, march, 1850), token)).AsT0;
        (await context.Set<BudgetCheckpoint>().CountAsync(token)).ShouldBe(3);
        await VerifyMonthAsync(database, plan, march, token);
        plan = (await database.Store.ExecuteAsync("owner-a", plan.Id, new AssignMoney(plan.Version, category.Id, march.AddMonths(-2), 1900), token)).AsT0;
        (await context.Set<BudgetCheckpoint>().CountAsync(token)).ShouldBe(1);
        (await context.Set<BudgetCheckpoint>().AsNoTracking().SingleAsync(token)).State.ShouldBe(january.State);
        await VerifyMonthAsync(database, plan, march, token);
        plan = (await database.Store.ExecuteAsync("owner-a", plan.Id, new UndoChange(plan.Version), token)).AsT0;
        (await context.Set<BudgetCheckpoint>().CountAsync(token)).ShouldBe(1);
        await VerifyMonthAsync(database, plan, march, token);
        plan = (await database.Store.ExecuteAsync("owner-a", plan.Id, new RedoChange(plan.Version), token)).AsT0;
        await VerifyMonthAsync(database, plan, march, token);
        plan = (await database.Store.ExecuteAsync("owner-a", plan.Id, new SaveCategory(plan.Version, category with
        {
            Target = new(TargetKind.Refill, TargetCadence.Yearly, 24000, march.AddMonths(-1), march.AddMonths(9)),
        }), token)).AsT0;
        (await context.Set<BudgetCheckpoint>().CountAsync(token)).ShouldBe(4);
        await VerifyMonthAsync(database, plan, march, token);
        var from = march.AddMonths(-1).AddDays(13);
        var through = march.AddDays(20);
        var full = (await database.Store.ReadAsync("owner-a", plan.Id, token)).ShouldNotBeNull();
        var reports = await database.Queries.ReportsAsync("owner-a", plan.Id, from, through, token);
        reports.Months.ShouldBe(ReportCalculator.Months(full, from, through));
        reports.Spending.ShouldBe(ReportCalculator.Spending(full, from, through));
    }

    [Fact]
    public async Task StaleOrForeignCheckpointPublicationCannotResurrectInvalidatedStateAsync()
    {
        var token = TestContext.Current.CancellationToken;
        await using var database = await BudgetDatabase.CreateAsync(token);
        var plan = await SeedAsync(database, token);
        var month = new DateOnly(2026, 3, 1);
        await VerifyMonthAsync(database, plan, month, token);
        await using var context = await database.Factory.CreateDbContextAsync(token);
        var oldStates = (await context.Set<BudgetCheckpoint>().AsNoTracking().ToArrayAsync(token))
            .Select(item => JsonSerializer.Deserialize<BudgetMonthState>(item.State)!).ToArray();
        var updated = (await database.Store.ExecuteAsync("owner-a", plan.Id, new AssignMoney(plan.Version, plan.Categories[0].Id, month.AddMonths(-2), 2000), token)).AsT0;
        await BudgetCheckpoints.SaveAsync(database.Factory, "owner-a", plan.Id, plan.Version, oldStates, token);
        await BudgetCheckpoints.SaveAsync(database.Factory, "owner-b", plan.Id, updated.Version, oldStates, token);
        (await context.Set<BudgetCheckpoint>().CountAsync(token)).ShouldBe(1);
        await Task.WhenAll(VerifyMonthAsync(database, updated, month, token), VerifyMonthAsync(database, updated, month, token));
        (await context.Set<BudgetCheckpoint>().CountAsync(token)).ShouldBe(4);
        (await database.Queries.RevisionAsync("owner-a", plan.Id, token)).ShouldBe(updated.Version);
    }

    [Fact]
    public async Task OlderCheckpointFormatsAreIgnoredReplacedAndCascadeWithPlanDeletionAsync()
    {
        var token = TestContext.Current.CancellationToken;
        await using var database = await BudgetDatabase.CreateAsync(token);
        var plan = await SeedAsync(database, token);
        var month = new DateOnly(2026, 3, 1);
        await VerifyMonthAsync(database, plan, month, token);
        await using var context = await database.Factory.CreateDbContextAsync(token);
        await context.Set<BudgetCheckpoint>().ExecuteUpdateAsync(update => update.SetProperty(item => item.FormatVersion, 0).SetProperty(item => item.State, "{}"), token);
        await context.Set<BudgetPlan>().ExecuteUpdateAsync(update => update.SetProperty(item => item.Version, 1), token);
        await VerifyMonthAsync(database, plan with { Version = 1 }, month, token);
        (await context.Set<BudgetCheckpoint>().Select(item => item.FormatVersion).ToArrayAsync(token)).ShouldBe([1, 1, 1, 1]);
        await context.Set<BudgetPlan>().ExecuteDeleteAsync(token);
        (await context.Set<BudgetCheckpoint>().CountAsync(token)).ShouldBe(0);
    }

    private static async Task VerifyMonthAsync(BudgetDatabase database, PlanSnapshot plan, DateOnly month, CancellationToken token)
    {
        var full = (await database.Store.ReadAsync("owner-a", plan.Id, token)).ShouldNotBeNull();
        var actual = await database.Queries.MonthAsync("owner-a", plan.Id, month, plan.Version, token);
        var expected = BudgetCalculator.Calculate(full, month, full.Today);
        // SQL SUM and decimal arithmetic can retain different decimal scales.
        // Compare every numeric field and row, rather than JSON spelling of zero.
        (actual with { Categories = expected.Categories }).ShouldBe(expected);
        actual.Categories.ShouldBe(expected.Categories);
    }

    private static async Task<PlanSnapshot> SeedAsync(BudgetDatabase database, CancellationToken token)
    {
        var plan = HouseholdScenario.Create(3, 200);
        await using var context = await database.Factory.CreateDbContextAsync(token);
        context.Add(new BudgetPlan { Id = plan.Id, OwnerId = "owner-a", Name = plan.Name, CreatedOn = plan.CreatedOn });
        BudgetSnapshotMapping.Apply(context, plan);
        await context.SaveChangesAsync(token);
        return plan;
    }
}
