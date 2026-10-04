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
public sealed class AccountOperationTests
{
    [Fact]
    public async Task AccountAggregatesRespectDatesTransferSidesAndOverlappingForeignIdsAsync()
    {
        var token = TestContext.Current.CancellationToken;
        var probe = new DatabaseProbe();
        await using var database = await BudgetDatabase.CreateAsync(token, interceptor: probe);
        var plan = AccountScenario();
        await SeedAsync(database, plan, "owner-a", token);
        await SeedAsync(database, plan with { Id = Guid.NewGuid(), Transactions = plan.Transactions.Select(item => item with { Amount = item.Amount * 10 }).ToArray() }, "owner-b", token);
        await using var context = await database.Factory.CreateDbContextAsync(token);
        foreach (var account in plan.Accounts)
        {
            foreach (var through in new[] { plan.CreatedOn.AddDays(-1), plan.CreatedOn, plan.CreatedOn.AddDays(1), plan.Today, plan.Today.AddDays(1) })
            {
                probe.Start();
                var actual = await AccountLedgerQueries.ReadAsync(context, plan.Id, account.Id, through, token);
                probe.Stop();
                actual.ShouldBe(AccountLedgerFacts.FromLedger(plan, account.Id, through));
                probe.Materialized.ShouldBeEmpty();
                probe.Commands.Count.ShouldBe(1);
            }
        }
        (await AccountLedgerQueries.ReadAsync(context, plan.Id, plan.Accounts[0].Id, plan.Today, token)).ShouldBe(new(plan.CreatedOn, true, -25, 5));
        (await AccountLedgerQueries.ReadAsync(context, plan.Id, Guid.NewGuid(), plan.Today, token)).ShouldBe(new(null, false, 0, 0));
        foreach (var change in new[]
        {
            plan.Accounts[0] with { OpeningBalance = 101 },
            plan.Accounts[0] with { OpenedOn = plan.CreatedOn.AddDays(1) },
            plan.Accounts[0] with { Closed = true },
        })
        {
            (await database.Store.ExecuteAsync("owner-a", plan.Id, new SaveAccount(0, change), token)).IsT1.ShouldBeTrue();
            var unchanged = (await database.Store.ReadAsync("owner-a", plan.Id, token)).ShouldNotBeNull();
            unchanged.Version.ShouldBe(0);
            unchanged.Accounts.OrderBy(item => item.Id).ShouldBe(plan.Accounts.OrderBy(item => item.Id));
            ShouldHaveEntries(unchanged, plan);
        }
    }

    [Fact]
    public async Task ReconciliationReadsOnlyChangedEntriesAndRestoresThemWithoutTouchingOtherSidesAsync()
    {
        var token = TestContext.Current.CancellationToken;
        var probe = new DatabaseProbe();
        await using var database = await BudgetDatabase.CreateAsync(token, interceptor: probe);
        var plan = AccountScenario();
        await SeedAsync(database, plan, "owner-a", token);
        await VerifyMonthAsync(database, plan.Id, plan.CreatedOn, token);
        var openings = await CheckpointsAsync(database, plan.Id, token);
        openings.Length.ShouldBe(2);
        probe.Start();
        var changed = (await database.Store.ExecuteAsync("owner-a", plan.Id, new ReconcileAccount(0, plan.Accounts[0].Id, plan.Today, 102, true), token)).AsT0;
        probe.Stop();
        probe.Materialized.GetValueOrDefault(nameof(BudgetTransaction)).ShouldBe(3);
        changed.Transactions.Count.ShouldBe(4);
        var full = (await database.Store.ReadAsync("owner-a", plan.Id, token)).ShouldNotBeNull();
        var balance = BudgetFacts.Balance(full, full.Accounts[0], plan.Today);
        balance.Cleared.ShouldBe(102);
        balance.Working.ShouldBe(72);
        (await CheckpointsAsync(database, plan.Id, token)).ShouldBe(openings.Take(1));
        await VerifyMonthAsync(database, plan.Id, plan.CreatedOn, token);
        var expected = plan.Transactions.Select(item => item.Sequence switch
        {
            1 or 5 => item with { State = ClearingState.Reconciled },
            4 => item with { TransferState = ClearingState.Reconciled },
            _ => item,
        }).ToArray();
        var adjustment = full.Transactions.Single(item => item.Sequence == 9);
        adjustment.Amount.ShouldBe(-3);
        adjustment.Date.ShouldBe(plan.Today);
        adjustment.State.ShouldBe(ClearingState.Reconciled);
        adjustment.Splits.ShouldBeEmpty();
        ShouldHaveEntries(full with { Transactions = full.Transactions.Where(item => item.Id != adjustment.Id).ToArray() }, plan with { Transactions = expected });
        await using var context = await database.Factory.CreateDbContextAsync(token);
        var history = await context.Set<BudgetHistory>().SingleAsync(token);
        var patch = BudgetHistoryCodec.Deserialize(history).AsT0;
        patch.Before.Transactions.Count.ShouldBe(3);
        patch.After.Transactions.Count.ShouldBe(4);
        (await database.Store.ExecuteAsync("owner-a", plan.Id, new UndoChange(1), token)).IsT0.ShouldBeTrue();
        ShouldHaveEntries((await database.Store.ReadAsync("owner-a", plan.Id, token)).ShouldNotBeNull(), plan);
        (await CheckpointsAsync(database, plan.Id, token)).ShouldBe(openings.Take(1));
        (await database.Store.ExecuteAsync("owner-a", plan.Id, new RedoChange(2), token)).IsT0.ShouldBeTrue();
        ShouldHaveEntries((await database.Store.ReadAsync("owner-a", plan.Id, token)).ShouldNotBeNull(), full);
        probe.Start();
        (await database.Store.ExecuteAsync("owner-a", plan.Id, new ReconcileAccount(3, plan.Accounts[0].Id, plan.Today, 102, false), token)).AsT0.Transactions.ShouldBeEmpty();
        probe.Stop();
        probe.Materialized.GetValueOrDefault(nameof(BudgetTransaction)).ShouldBe(0);
        (await context.Set<BudgetHistory>().CountAsync(token)).ShouldBe(1);
        (await context.Set<BudgetReceipt>().CountAsync(token)).ShouldBe(4);
    }

    [Fact]
    public async Task MetadataAndClearingPreserveCheckpointContentsWhileMoneyChangesRebuildAsync()
    {
        var token = TestContext.Current.CancellationToken;
        var probe = new DatabaseProbe();
        await using var database = await BudgetDatabase.CreateAsync(token, interceptor: probe);
        var plan = HouseholdScenario.Create(3, 200);
        await SeedAsync(database, plan, "owner-a", token);
        var month = HouseholdScenario.Start.AddMonths(2);
        await VerifyMonthAsync(database, plan.Id, month, token);
        var saved = await CheckpointsAsync(database, plan.Id, token);
        saved.Length.ShouldBe(4);
        var version = 0L;
        var entry = plan.Transactions[0];
        var allocation = plan.Allocations.Single(item => item.Month == month && item.CategoryId == plan.Categories[0].Id);
        PlanCommand[] commands = [new SaveAccount(0, plan.Accounts[0] with { Name = "New checking", Notes = "Changed notes" }),
            new SaveAccount(1, plan.Accounts[2] with { Name = "New card" }),
            new SaveCategory(2, plan.Categories[0] with { Name = "New housing", Hidden = true, Target = new(TargetKind.Refill, TargetCadence.Yearly, 25000, plan.CreatedOn, plan.Today) }),
            new SaveGroup(3, plan.Groups[0] with { Name = "New group" }),
            new AssignMoney(4, allocation.CategoryId, month, allocation.Amount, true),
            new SaveTransaction(5, entry with { Memo = "Edited memo" }),
            new UpdateTransactionStates(6, [entry.Id], ClearingState.Uncleared, true, plan.Accounts[0].Id)];
        foreach (var command in commands)
        {
            probe.Start();
            (await database.Store.ExecuteAsync("owner-a", plan.Id, command, token)).AsT0.Version.ShouldBe(++version);
            probe.Stop();
            if (command is SaveAccount) { probe.Materialized.GetValueOrDefault(nameof(BudgetTransaction)).ShouldBe(0); }
            (await CheckpointsAsync(database, plan.Id, token)).ShouldBe(saved);
            await VerifyMonthAsync(database, plan.Id, month, token);
        }
        (await database.Store.ExecuteAsync("owner-a", plan.Id, new UndoChange(version++), token)).IsT0.ShouldBeTrue();
        (await CheckpointsAsync(database, plan.Id, token)).ShouldBe(saved);
        (await database.Store.ExecuteAsync("owner-a", plan.Id, new RedoChange(version++), token)).IsT0.ShouldBeTrue();
        (await CheckpointsAsync(database, plan.Id, token)).ShouldBe(saved);
        var current = (await database.Store.ReadAsync("owner-a", plan.Id, token)).ShouldNotBeNull();
        current.Categories.Single(item => item.CreditAccountId == plan.Accounts[2].Id).Name.ShouldBe("New card");
        var income = current.Transactions.Single(item => item.Id == entry.Id);
        (await database.Store.ExecuteAsync("owner-a", plan.Id, new SaveTransaction(version++, income with
        {
            Amount = income.Amount + 1,
            Splits = [income.Splits[0] with { Amount = income.Amount + 1 }]
        }), token)).IsT0.ShouldBeTrue();
        (await CheckpointsAsync(database, plan.Id, token)).ShouldBe(saved.Take(1));
        await VerifyMonthAsync(database, plan.Id, month, token);
        (await database.Store.ExecuteAsync("owner-a", plan.Id, new UndoChange(version++), token)).IsT0.ShouldBeTrue();
        (await CheckpointsAsync(database, plan.Id, token)).ShouldBe(saved.Take(1));
        await VerifyMonthAsync(database, plan.Id, month, token);
        (await database.Store.ExecuteAsync("owner-a", plan.Id, new SaveAccount(version, current.Accounts[0] with { OpeningBalance = 6100 }), token)).IsT0.ShouldBeTrue();
        (await CheckpointsAsync(database, plan.Id, token)).ShouldBeEmpty();
        await VerifyMonthAsync(database, plan.Id, month, token);
    }

    private static PlanSnapshot AccountScenario()
    {
        var plan = HouseholdScenario.Create(1, 200);
        var first = plan.CreatedOn;
        var accounts = plan.Accounts.Take(2).Select((item, index) => item with { OpeningBalance = (index + 1) * 100 }).ToArray();
        TransactionData Entry(int sequence, int account, decimal amount, ClearingState state, DateOnly date, int? transfer = null, ClearingState transferState = ClearingState.Uncleared, RepeatFrequency repeat = RepeatFrequency.None)
            => new(HouseholdScenario.Id(90_000 + sequence), accounts[account].Id, date, "Account test", "", amount, transfer.HasValue ? accounts[transfer.Value].Id : null,
                state, transferState, false, "", [], repeat, repeat == RepeatFrequency.None ? null : date, Sequence: sequence);
        return plan with
        {
            Accounts = accounts,
            Groups = [],
            Categories = [],
            Allocations = [],
            Transactions = [Entry(1, 0, -10, ClearingState.Cleared, first.AddDays(1)), Entry(2, 0, -20, ClearingState.Reconciled, first.AddDays(2)),
                Entry(3, 0, -30, ClearingState.Uncleared, first.AddDays(3)), Entry(4, 1, -40, ClearingState.Reconciled, first.AddDays(4), 0, ClearingState.Cleared),
                Entry(5, 0, -5, ClearingState.Cleared, first.AddDays(5), 1, ClearingState.Reconciled), Entry(6, 0, -50, ClearingState.Cleared, first.AddMonths(1)),
                Entry(7, 0, -999, ClearingState.Uncleared, first, repeat: RepeatFrequency.Monthly), Entry(8, 1, -888, ClearingState.Cleared, first.AddDays(1))],
        };
    }

    private static async Task SeedAsync(BudgetDatabase database, PlanSnapshot plan, string owner, CancellationToken token)
    {
        await using var context = await database.Factory.CreateDbContextAsync(token);
        context.Add(new BudgetPlan { Id = plan.Id, OwnerId = owner, Name = plan.Name, CreatedOn = plan.CreatedOn, NextSequence = plan.Transactions.Count });
        BudgetSnapshotMapping.Apply(context, plan with { Transactions = plan.Transactions.Select((entry, index) => entry with { Sequence = index + 1 }).ToArray() });
        await context.SaveChangesAsync(token);
    }

    private static void ShouldHaveEntries(PlanSnapshot actual, PlanSnapshot expected)
    {
        actual.Transactions.Count.ShouldBe(expected.Transactions.Count);
        foreach (var (entry, wanted) in actual.Transactions.OrderBy(item => item.Sequence).Zip(expected.Transactions.OrderBy(item => item.Sequence)))
        {
            (entry with { Splits = wanted.Splits }).ShouldBe(wanted);
            entry.Splits.OrderBy(item => item.Id).ShouldBe(wanted.Splits.OrderBy(item => item.Id));
        }
    }
    private static async Task<string[]> CheckpointsAsync(BudgetDatabase database, Guid id, CancellationToken token)
    {
        await using var context = await database.Factory.CreateDbContextAsync(token);
        return await context.Set<BudgetCheckpoint>().Where(item => item.PlanId == id).OrderBy(item => item.Month).Select(item => item.State).ToArrayAsync(token);
    }
    private static async Task VerifyMonthAsync(BudgetDatabase database, Guid id, DateOnly month, CancellationToken token)
    {
        var full = (await database.Store.ReadAsync("owner-a", id, token)).ShouldNotBeNull();
        var expected = BudgetCalculator.Calculate(full, month, full.Today);
        var actual = await database.Queries.MonthAsync("owner-a", id, month, full.Version, token);
        (actual with { Categories = expected.Categories }).ShouldBe(expected);
        actual.Categories.ShouldBe(expected.Categories);
    }
}
