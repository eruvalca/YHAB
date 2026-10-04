using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;
using Xunit;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.IntegrationTests;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class WorkspaceProjectionTests
{
    [Fact]
    public async Task ConcurrentWriteCannotMixNavigationAndMonthRevisionsAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var token = timeout.Token;
        var barrier = new ReadBarrier();
        await using var database = await BudgetDatabase.CreateAsync(token, interceptor: barrier);
        var fixture = await LedgerScaleFixture.CreateAsync(database, 120, 2, 2, 4, token);
        barrier.Arm();
        var reading = database.Queries.WorkspaceAsync("owner-a", fixture.Id, null, token);
        try
        {
            await barrier.Started.Task.WaitAsync(token);
            var entry = new TransactionData(Guid.NewGuid(), fixture.Accounts[0].Id, fixture.Month, "Gift", "", 25, null,
                ClearingState.Cleared, ClearingState.Uncleared, false, "", [new(Guid.NewGuid(), null, 25, "")]);
            (await database.Store.ExecuteAsync("owner-a", fixture.Id, new SaveTransaction(0, entry), token)).IsT0.ShouldBeTrue();
        }
        finally { barrier.Release.TrySetResult(); }
        var original = await reading;
        original.View.Catalog.Version.ShouldBe(0);
        original.View.Catalog.Transactions.ShouldBeEmpty();
        original.View.Catalog.Allocations.ShouldBeEmpty();
        original.Month.Month.ShouldBe(fixture.Month);
        original.View.Balances.Sum(item => item.Working).ShouldBe(2_001_092);
        (original.Month.ReadyToAssign + original.Month.Available).ShouldBe(2_001_092);

        var latest = await database.Queries.WorkspaceAsync("owner-a", fixture.Id, null, token);
        latest.View.Catalog.Version.ShouldBe(1);
        latest.View.Balances.Sum(item => item.Working).ShouldBe(2_001_117);
        (latest.Month.ReadyToAssign + latest.Month.Available).ShouldBe(2_001_117);
        latest.Month.ReadyToAssign.ShouldBe(original.Month.ReadyToAssign + 25);
        latest.Month.Available.ShouldBe(original.Month.Available);
        // A returned snapshot remains internally consistent even when immediately
        // superseded. User writes and explicitly versioned reads still reject it.
        (await database.Store.ExecuteAsync("owner-a", fixture.Id, new AssignMoney(0, fixture.Categories[0].Id, fixture.Month, 10), token)).IsT3.ShouldBeTrue();
        (await Should.ThrowAsync<BudgetRequestException>(() => database.Queries.MonthAsync("owner-a", fixture.Id, fixture.Month, 0, token))).Status.ShouldBe(409);
    }

    [Fact]
    public async Task CachedWorkspaceRemainsOwnerScopedAndUsesTheRequestedMonthAsync()
    {
        var token = TestContext.Current.CancellationToken;
        await using var database = await BudgetDatabase.CreateAsync(token);
        var fixture = await LedgerScaleFixture.CreateAsync(database, 120, 2, 2, 4, token);
        var date = fixture.Month.AddMonths(-1).AddDays(10);
        var first = await database.Queries.WorkspaceAsync("owner-a", fixture.Id, date, token);
        first.Month.Month.ShouldBe(fixture.Month.AddMonths(-1));
        var repeated = await database.Queries.WorkspaceAsync("owner-a", fixture.Id, date, token);
        repeated.Month.ShouldBeSameAs(first.Month);
        var full = (await database.Store.ReadAsync("owner-a", fixture.Id, token)).ShouldNotBeNull();
        var expected = BudgetCalculator.Calculate(full, BudgetFacts.Month(date), full.Today);
        (first.Month with { Categories = expected.Categories }).ShouldBe(expected);
        first.Month.Categories.ShouldBe(expected.Categories);
        foreach (var (owner, id) in new[] { ("owner-b", fixture.Id), ("owner-a", Guid.NewGuid()) })
        {
            (await Should.ThrowAsync<BudgetRequestException>(() => database.Queries.WorkspaceAsync(owner, id, date, token))).Status.ShouldBe(404);
        }
        foreach (var invalid in new[] { new DateOnly(1999, 12, 31), new DateOnly(2101, 1, 1) })
        {
            (await Should.ThrowAsync<BudgetRequestException>(() => database.Queries.WorkspaceAsync("owner-a", fixture.Id, invalid, token))).Status.ShouldBe(400);
        }
        (await database.Queries.RevisionAsync("owner-a", fixture.Id, token)).ShouldBe(0);
    }

    private sealed class ReadBarrier : DbCommandInterceptor
    {
        private int _armed;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Arm() => Interlocked.Exchange(ref _armed, 1);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"BudgetAccount\"", StringComparison.Ordinal) && Interlocked.Exchange(ref _armed, 0) == 1)
            {
                Started.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
