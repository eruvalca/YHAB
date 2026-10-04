using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;
using Xunit;
using YHAB.Features.Budgeting.Data;
using YHAB.Features.Budgeting.Services;
using YHAB.SharedKernel.Budgeting;
using YHAB.Tests.Scenarios;

namespace YHAB.IntegrationTests;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class ReportProjectionTests(ITestOutputHelper output)
{
    [Fact]
    public async Task AggregatesMatchFullHistoryAcrossPartialMonthsTransfersAndOpeningDatesAsync()
    {
        var token = TestContext.Current.CancellationToken;
        var probe = new DatabaseProbe();
        await using var database = await BudgetDatabase.CreateAsync(token, interceptor: probe);
        var plan = CreateScenario();
        await SeedAsync(database, plan, "owner-a", token);
        // Overlapping account, transaction and split IDs detect missing plan scope
        // in either the account movement query or the split join.
        await SeedAsync(database, plan with
        {
            Id = Guid.NewGuid(),
            Transactions = plan.Transactions.Select(item => item with { Amount = item.Amount * 10, Splits = item.Splits.Select(split => split with { Amount = split.Amount * 10 }).ToArray() }).ToArray(),
        }, "owner-b", token);
        var first = HouseholdScenario.Start;
        var ranges = new[]
        {
            (first, HouseholdScenario.End(3)),
            (first.AddDays(14), first.AddMonths(2).AddDays(19)),
            (first.AddMonths(1).AddDays(13), first.AddMonths(1).AddDays(13)),
            (first.AddMonths(1).AddDays(14), first.AddMonths(1).AddDays(14)),
            (first.AddMonths(-1), first.AddDays(-1)),
            (first.AddMonths(3), first.AddMonths(3)),
        };
        foreach (var (from, through) in ranges)
        {
            probe.Start();
            var actual = await database.Queries.ReportsAsync("owner-a", plan.Id, from, through, token);
            output.WriteLine($"{from:yyyy-MM-dd} through {through:yyyy-MM-dd}: {probe.Stop()}");
            actual.Version.ShouldBe(0);
            actual.Months.ShouldBe(ReportCalculator.Months(plan, from, through));
            actual.Spending.ShouldBe(ReportCalculator.Spending(plan, from, through));
            probe.Materialized.GetValueOrDefault(nameof(BudgetTransaction)).ShouldBe(0);
            probe.Materialized.GetValueOrDefault(nameof(BudgetSplit)).ShouldBe(0);
            probe.Materialized.GetValueOrDefault(nameof(BudgetAllocation)).ShouldBe(0);
            probe.Commands.Count(sql => sql.Contains("\"BudgetTransaction\"", StringComparison.Ordinal)).ShouldBe(2);
        }
        var full = await database.Queries.ReportsAsync("owner-a", plan.Id, first, HouseholdScenario.End(3), token);
        full.Months.Sum(item => item.Income).ShouldBe(25_225);
        full.Months.Sum(item => item.Expense).ShouldBe(11_770);
        full.Months[^1].NetWorth.ShouldBe(31_217);
        full.Spending.Sum(item => item.Amount).ShouldBe(11_770);
        var future = await database.Queries.ReportsAsync("owner-a", plan.Id, first.AddMonths(3), first.AddMonths(3), token);
        future.Months.Single().ShouldBe(new(first.AddMonths(3), 0, 0, 43_566, 11_350));
        (await Should.ThrowAsync<BudgetRequestException>(() => database.Queries.ReportsAsync("owner-b", plan.Id, first, first, token))).Status.ShouldBe(404);
    }

    [Fact]
    public async Task ReportReadsOneRevisionAcrossConcurrentTransactionChangesAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var token = timeout.Token;
        var barrier = new ReportReadBarrier();
        await using var database = await BudgetDatabase.CreateAsync(token, interceptor: barrier);
        var plan = CreateScenario();
        await SeedAsync(database, plan, "owner-a", token);
        var from = HouseholdScenario.Start;
        var through = HouseholdScenario.End(3);
        barrier.Armed = true;
        var pending = database.Queries.ReportsAsync("owner-a", plan.Id, from, through, token);
        try
        {
            await barrier.ReadStarted.Task.WaitAsync(token);
            var income = new TransactionData(Guid.Empty, plan.Accounts[0].Id, from, "Concurrent income", "", 100, null,
                ClearingState.Uncleared, ClearingState.Uncleared, false, "", [new(Guid.Empty, null, 100, "")]);
            (await database.Store.ExecuteAsync("owner-a", plan.Id, new SaveTransaction(0, income), token)).IsT0.ShouldBeTrue();
        }
        finally
        {
            barrier.Continue.TrySetResult();
            await pending;
        }
        var original = await pending;
        original.Version.ShouldBe(0);
        original.Months.ShouldBe(ReportCalculator.Months(plan, from, through));
        original.Spending.ShouldBe(ReportCalculator.Spending(plan, from, through));
        var updated = await database.Queries.ReportsAsync("owner-a", plan.Id, from, through, token);
        updated.Version.ShouldBe(1);
        updated.Months[0].Income.ShouldBe(original.Months[0].Income + 100);
        updated.Months[^1].NetWorth.ShouldBe(original.Months[^1].NetWorth + 100);
        updated.Spending.ShouldBe(original.Spending);
    }

    [Fact]
    public async Task EmptyReportsAndDateBoundariesPreserveTheExistingRequestContractAsync()
    {
        var token = TestContext.Current.CancellationToken;
        await using var database = await BudgetDatabase.CreateAsync(token);
        var id = await database.Store.CreateAsync("owner-a", new("Empty reports", StarterCategories: false), token);
        var from = new DateOnly(2024, 1, 1);
        var report = await database.Queries.ReportsAsync("owner-a", id, from, from.AddDays(732), token);
        report.Months.Count.ShouldBe(25);
        report.Months.ShouldAllBe(item => item.Income == 0 && item.Expense == 0 && item.Assets == 0 && item.Debt == 0);
        report.Spending.ShouldBeEmpty();
        foreach (var (start, end) in new[] { (new DateOnly(1999, 12, 31), from), (from, new DateOnly(2101, 1, 1)), (from, from.AddDays(-1)), (from, from.AddDays(733)) })
        {
            (await Should.ThrowAsync<BudgetRequestException>(() => database.Queries.ReportsAsync("owner-a", id, start, end, token))).Status.ShouldBe(400);
        }
    }

    private static PlanSnapshot CreateScenario()
    {
        var plan = HouseholdScenario.Create(3, 1000);
        var february = HouseholdScenario.Start.AddMonths(1);
        var entries = plan.Transactions.ToList();
        void Transfer(int id, DateOnly date, decimal amount, Guid? category, int destination)
            => entries.Add(new(HouseholdScenario.Id(id), plan.Accounts[4].Id, date, "Tracking transfer", "", amount, plan.Accounts[destination].Id,
                ClearingState.Cleared, ClearingState.Uncleared, true, "", destination == 5 ? [] : [new(HouseholdScenario.Id(id + 100), category, amount, "")]));
        Transfer(90001, february.AddDays(13), -25, null, 0);
        Transfer(90002, february.AddDays(14), 10, plan.Categories[2].Id, 0);
        Transfer(90003, february.AddDays(15), -50, null, 5);
        entries.Add(new(HouseholdScenario.Id(90004), plan.Accounts[0].Id, february, "Template excluded", "", -99_999, null,
            ClearingState.Uncleared, ClearingState.Uncleared, false, "", [new(HouseholdScenario.Id(90104), plan.Categories[0].Id, -99_999, "")], RepeatFrequency.Daily, february));
        return plan with
        {
            Accounts = [.. plan.Accounts, new(HouseholdScenario.Id(7), "Opened later", AccountKind.Asset, 777, february.AddDays(14), false, ""),
                new(HouseholdScenario.Id(8), "Future opening", AccountKind.Asset, 999, february.AddMonths(2), false, "")],
            Transactions = entries.Select((entry, index) => entry with { Sequence = index + 1 }).ToArray(),
        };
    }

    private static async Task SeedAsync(BudgetDatabase database, PlanSnapshot plan, string owner, CancellationToken token)
    {
        await using var context = await database.Factory.CreateDbContextAsync(token);
        context.Add(new BudgetPlan { Id = plan.Id, OwnerId = owner, Name = plan.Name, CreatedOn = plan.CreatedOn, NextSequence = plan.Transactions.Count });
        BudgetSnapshotMapping.Apply(context, plan);
        await context.SaveChangesAsync(token);
    }

    private sealed class ReportReadBarrier : DbCommandInterceptor
    {
        public bool Armed { get; set; }
        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (Armed && command.CommandText.Contains("UNION ALL", StringComparison.Ordinal))
            {
                Armed = false;
                ReadStarted.TrySetResult();
                await Continue.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
