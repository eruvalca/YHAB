using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;
using Xunit;
using YHAB.Features.Budgeting.Data;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.IntegrationTests;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class PayeePersistenceTests
{
    [Fact]
    public async Task RenameAndHistoryPreserveCheckpointsAndLaterAutomaticFieldsAsync()
    {
        var token = TestContext.Current.CancellationToken;
        var probe = new DatabaseProbe();
        await using var database = await BudgetDatabase.CreateAsync(token, interceptor: probe);
        var fixture = await LedgerScaleFixture.CreateAsync(database, 1000, 2, 3, 24, token);
        var initial = (await database.Store.ReadAsync("owner-a", fixture.Id, token)).ShouldNotBeNull();
        var templateId = initial.Transactions.Single(item => item.Sequence == 1).Id;
        await using (var context = await database.Factory.CreateDbContextAsync(token))
        {
            await context.Database.ExecuteSqlAsync($"""
                UPDATE "BudgetTransaction" SET "Payee" = CASE WHEN "Sequence" % 3 = 0 THEN 'MARKET' WHEN "Sequence" % 3 = 1 THEN 'market' ELSE 'Market' END
                WHERE "PlanId" = {fixture.Id} AND "Payee" = 'Market';
                UPDATE "BudgetTransaction" SET "Date" = {initial.Today}, "AnchorDate" = {initial.Today}, "Repeat" = {(int)RepeatFrequency.Daily}
                WHERE "PlanId" = {fixture.Id} AND "Id" = {templateId};
                """, token);
        }
        initial = (await database.Store.ReadAsync("owner-a", fixture.Id, token)).ShouldNotBeNull();
        var foreignId = await database.Store.CreateAsync("owner-b", new("Foreign names", StarterCategories: false), token);
        await using (var context = await database.Factory.CreateDbContextAsync(token))
        {
            BudgetSnapshotMapping.Apply(context, initial with { Id = foreignId });
            await context.SaveChangesAsync(token);
        }
        await database.Queries.MonthAsync("owner-a", fixture.Id, fixture.Month, 0, token);
        var checkpoints = await CheckpointsAsync(database, fixture.Id, token);
        checkpoints.Length.ShouldBe(25);
        var rename = new RenamePayee(0, "mArKeT", " Corner store ");
        var commands = new PlanCommand[] { rename, new UndoChange(1), new RedoChange(2) };
        foreach (var command in commands)
        {
            probe.Start();
            var result = (await database.Store.ExecuteAsync("owner-a", fixture.Id, command, token)).AsT0;
            probe.Stop();
            result.Version.ShouldBe(command.Version + 1);
            result.Transactions.ShouldBeEmpty();
            probe.Materialized.GetValueOrDefault(nameof(BudgetTransaction)).ShouldBe(0);
            probe.Materialized.GetValueOrDefault(nameof(BudgetSplit)).ShouldBe(0);
            probe.Commands.Count(sql => sql.StartsWith("UPDATE \"BudgetTransaction\"", StringComparison.Ordinal)).ShouldBe(1);
            (await CheckpointsAsync(database, fixture.Id, token)).ShouldBe(checkpoints);
            var current = (await database.Store.ReadAsync("owner-a", fixture.Id, token)).ShouldNotBeNull();
            var expected = command is UndoChange ? initial.Transactions : initial.Transactions.Select(item =>
                string.Equals(item.Payee, "Market", StringComparison.OrdinalIgnoreCase) ? item with { Payee = "Corner store" } : item).ToArray();
            Entries(current.Transactions).ShouldBe(Entries(expected));
            current.Accounts.Sum(account => BudgetFacts.Balance(current, account, current.Today).Working).ShouldBe(fixture.Cash + 1);
        }
        (await database.Store.ExecuteAsync("owner-a", fixture.Id, rename, token)).AsT0.Version.ShouldBe(3);
        (await database.Store.ExecuteAsync("owner-a", fixture.Id, rename with { NewName = "Different" }, token)).IsT1.ShouldBeTrue();
        (await database.Store.ExecuteAsync("owner-b", fixture.Id, new RenamePayee(3, "Corner store", "Foreign"), token)).IsT2.ShouldBeTrue();
        await using (var context = await database.Factory.CreateDbContextAsync(token))
        {
            var history = await context.Set<BudgetHistory>().SingleAsync(item => item.PlanId == fixture.Id, token);
            history.Before.ShouldContain("payee-names-v1");
            history.Before.ShouldNotContain("accountId");
            history.After.ShouldNotContain("splits");
            (history.Before.Length + history.After.Length).ShouldBeLessThan(160_000);
            (await context.Set<BudgetReceipt>().CountAsync(item => item.PlanId == fixture.Id, token)).ShouldBe(3);
        }

        (await database.Store.PostRecurringAsync("owner-a", fixture.Id, initial.Today, token)).ShouldBeTrue();
        (await database.Store.ExecuteAsync("owner-a", fixture.Id, new UndoChange(4), token)).IsT0.ShouldBeTrue();
        var undone = (await database.Store.ReadAsync("owner-a", fixture.Id, token)).ShouldNotBeNull();
        var expectedOriginal = initial.Transactions.Select(item => item.Id == templateId
            ? item with { Date = initial.Today.AddDays(1), Occurrence = 1 } : item).ToArray();
        Entries(undone.Transactions.Where(item => item.SourceTemplateId is null)).ShouldBe(Entries(expectedOriginal));
        var occurrence = undone.Transactions.Single(item => item.SourceTemplateId == templateId);
        occurrence.Payee.ShouldBe("Corner store");
        occurrence.Date.ShouldBe(initial.Today);
        occurrence.ScheduledDate.ShouldBe(initial.Today);
        occurrence.Amount.ShouldBe(-1);
        undone.Accounts.Sum(account => BudgetFacts.Balance(undone, account, undone.Today).Working).ShouldBe(fixture.Cash);
        (await database.Store.ExecuteAsync("owner-a", fixture.Id, new RedoChange(5), token)).IsT0.ShouldBeTrue();
        var redone = (await database.Store.ReadAsync("owner-a", fixture.Id, token)).ShouldNotBeNull();
        redone.Transactions.Count.ShouldBe(1001);
        redone.Transactions.Single(item => item.Id == templateId).Date.ShouldBe(initial.Today.AddDays(1));
        redone.Transactions.Count(item => string.Equals(item.Payee, "Corner store", StringComparison.Ordinal)).ShouldBe(901);
        Entries(redone.Transactions.Where(item => item.Id == occurrence.Id)).ShouldBe(Entries([occurrence]));
        var foreign = (await database.Store.ReadAsync("owner-b", foreignId, token)).ShouldNotBeNull();
        foreign.Version.ShouldBe(0);
        Entries(foreign.Transactions).ShouldBe(Entries(initial.Transactions));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HistoryRejectsChangedOrRemovedPayeesWithoutPartialWritesAsync(bool removed)
    {
        var token = TestContext.Current.CancellationToken;
        await using var database = await BudgetDatabase.CreateAsync(token);
        var fixture = await LedgerScaleFixture.CreateAsync(database, 20, 2, 3, 2, token);
        (await database.Store.ExecuteAsync("owner-a", fixture.Id, new RenamePayee(0, "Market", "Corner"), token)).IsT0.ShouldBeTrue();
        await using var context = await database.Factory.CreateDbContextAsync(token);
        var entry = context.Set<BudgetTransaction>().Where(item => item.PlanId == fixture.Id && item.Sequence == 1);
        if (removed) { await entry.ExecuteDeleteAsync(token); }
        else { await entry.ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Payee, "Later name"), token); }
        await context.Set<BudgetPlan>().Where(item => item.Id == fixture.Id).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Version, 2), token);
        var before = Snapshot((await database.Store.ReadAsync("owner-a", fixture.Id, token)).ShouldNotBeNull());
        var result = await database.Store.ExecuteAsync("owner-a", fixture.Id, new UndoChange(2), token);
        result.IsT1.ShouldBeTrue();
        result.AsT1.Message.ShouldContain("payee changed");
        Snapshot((await database.Store.ReadAsync("owner-a", fixture.Id, token)).ShouldNotBeNull()).ShouldBe(before);
        (await context.Set<BudgetPlan>().SingleAsync(item => item.Id == fixture.Id, token)).HistoryCursor.ShouldBe(1);
        (await context.Set<BudgetReceipt>().CountAsync(token)).ShouldBe(1);
    }

    [Fact]
    public async Task CompetingBulkRenamesCommitOneCompleteResultAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var token = timeout.Token;
        var barrier = new PlanClaimBarrier(0);
        await using var database = await BudgetDatabase.CreateAsync(token, interceptor: barrier);
        var fixture = await LedgerScaleFixture.CreateAsync(database, 1000, 2, 3, 2, token);
        barrier.Armed = true;
        var results = await Task.WhenAll(database.Store.ExecuteAsync("owner-a", fixture.Id, new RenamePayee(0, "Market", "First"), token),
            database.Store.ExecuteAsync("owner-a", fixture.Id, new RenamePayee(0, "Market", "Second"), token));
        results.Count(result => result.IsT0).ShouldBe(1);
        results.Count(result => result.IsT3).ShouldBe(1);
        var snapshot = (await database.Store.ReadAsync("owner-a", fixture.Id, token)).ShouldNotBeNull();
        snapshot.Version.ShouldBe(1);
        snapshot.Transactions.Count(item => string.Equals(item.Payee, results[0].IsT0 ? "First" : "Second", StringComparison.Ordinal)).ShouldBe(900);
        snapshot.Transactions.Count(item => string.Equals(item.Payee, "Salary", StringComparison.Ordinal)).ShouldBe(100);
        await using var context = await database.Factory.CreateDbContextAsync(token);
        (await context.Set<BudgetHistory>().CountAsync(token)).ShouldBe(1);
        (await context.Set<BudgetReceipt>().CountAsync(token)).ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailureAfterBulkUpdateRollsBackNamesCursorAndReceiptAndCanRetryAsync(bool undo)
    {
        var token = TestContext.Current.CancellationToken;
        var fault = new PayeeWriteFault();
        await using var database = await BudgetDatabase.CreateAsync(token, interceptor: fault);
        var fixture = await LedgerScaleFixture.CreateAsync(database, 1000, 2, 3, 2, token);
        var rename = new RenamePayee(0, "Market", "Corner");
        if (undo) { (await database.Store.ExecuteAsync("owner-a", fixture.Id, rename, token)).IsT0.ShouldBeTrue(); }
        PlanCommand command = undo ? new UndoChange(1) : rename;
        await database.Queries.MonthAsync("owner-a", fixture.Id, fixture.Month, command.Version, token);
        var before = Snapshot((await database.Store.ReadAsync("owner-a", fixture.Id, token)).ShouldNotBeNull());
        var checkpoints = await CheckpointsAsync(database, fixture.Id, token);
        fault.Armed = true;
        (await Should.ThrowAsync<InvalidOperationException>(() => database.Store.ExecuteAsync("owner-a", fixture.Id, command, token)))
            .Message.ShouldBe("Injected failure after payee SQL");
        fault.Armed.ShouldBeFalse();
        Snapshot((await database.Store.ReadAsync("owner-a", fixture.Id, token)).ShouldNotBeNull()).ShouldBe(before);
        (await CheckpointsAsync(database, fixture.Id, token)).ShouldBe(checkpoints);
        await using var context = await database.Factory.CreateDbContextAsync(token);
        (await context.Set<BudgetReceipt>().CountAsync(token)).ShouldBe(undo ? 1 : 0);
        (await context.Set<BudgetPlan>().AsNoTracking().SingleAsync(token)).HistoryCursor.ShouldBe(undo ? 1 : 0);
        (await database.Store.ExecuteAsync("owner-a", fixture.Id, command, token)).AsT0.Version.ShouldBe(command.Version + 1);
        (await context.Set<BudgetTransaction>().CountAsync(item => item.Payee == (undo ? "Market" : "Corner"), token)).ShouldBe(900);
        (await context.Set<BudgetReceipt>().CountAsync(token)).ShouldBe(undo ? 2 : 1);
        (await context.Set<BudgetHistory>().CountAsync(token)).ShouldBe(1);
    }

    [Theory]
    [InlineData("Café", "CAFÉ", true)]
    [InlineData("Café", "Cafe", false)]
    [InlineData("%_\\", "%_\\", true)]
    public async Task MatchingUsesOrdinalCaseRulesAndLiteralParameterizedNamesAsync(string stored, string requested, bool matches)
    {
        var token = TestContext.Current.CancellationToken;
        await using var database = await BudgetDatabase.CreateAsync(token);
        var fixture = await LedgerScaleFixture.CreateAsync(database, 10, 2, 3, 2, token);
        await using var context = await database.Factory.CreateDbContextAsync(token);
        await context.Set<BudgetTransaction>().Where(item => item.Payee == "Market").ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Payee, stored), token);
        (await database.Store.ExecuteAsync("owner-a", fixture.Id, new RenamePayee(0, requested, " Bob's %_\\ store "), token)).IsT0.ShouldBeTrue();
        (await context.Set<BudgetTransaction>().CountAsync(item => item.Payee == "Bob's %_\\ store", token)).ShouldBe(matches ? 9 : 0);
        (await context.Set<BudgetTransaction>().CountAsync(item => item.Payee == stored, token)).ShouldBe(matches ? 0 : 9);
        (await context.Set<BudgetHistory>().CountAsync(token)).ShouldBe(matches ? 1 : 0);
    }

    private static string Entries(IEnumerable<TransactionData> entries) => JsonSerializer.Serialize(entries.OrderBy(item => item.Sequence));

    // Ledger reads have no presentation ordering contract; rolled-back SQL can
    // change PostgreSQL's chosen access path without changing a single value.
    private static string Snapshot(PlanSnapshot snapshot) => JsonSerializer.Serialize(snapshot with { Transactions = snapshot.Transactions.OrderBy(item => item.Sequence).ToArray() });

    private static async Task<string[]> CheckpointsAsync(BudgetDatabase database, Guid id, CancellationToken token)
    {
        await using var context = await database.Factory.CreateDbContextAsync(token);
        return await context.Set<BudgetCheckpoint>().Where(item => item.PlanId == id).OrderBy(item => item.Month).Select(item => item.State).ToArrayAsync(token);
    }

    private sealed class PayeeWriteFault : DbCommandInterceptor
    {
        public bool Armed { get; set; }
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (Armed && command.CommandText.StartsWith("UPDATE \"BudgetTransaction\"", StringComparison.Ordinal))
            {
                Armed = false;
                throw new InvalidOperationException("Injected failure after payee SQL");
            }
            return ValueTask.FromResult(result);
        }
    }
}
