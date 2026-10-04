using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;
using Xunit;
using YHAB.Features.Budgeting.Data;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.IntegrationTests;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class BackgroundPostingTests
{
    [Fact]
    public async Task CancelledBackgroundBatchReleasesItsLockWithoutPartialPostingAsync()
    {
        var token = TestContext.Current.CancellationToken;
        var pause = new PausedClaim();
        await using var database = await BudgetDatabase.CreateAsync(token, interceptor: pause);
        var id = await database.Store.CreateAsync("owner-a", new("Cancellation", StarterCategories: false), token);
        var today = (await database.Store.ReadAsync("owner-a", id, token)).ShouldNotBeNull().Today;
        var account = new AccountData(Guid.NewGuid(), "Tracking", AccountKind.Asset, 1000, today.AddDays(-199), false, "");
        (await database.Store.ExecuteAsync("owner-a", id, new SaveAccount(0, account), token)).IsT0.ShouldBeTrue();
        var template = new TransactionData(Guid.NewGuid(), account.Id, account.OpenedOn, "Daily", "", -1, null,
            ClearingState.Uncleared, ClearingState.Uncleared, false, "", [], RepeatFrequency.Daily);
        (await database.Store.ExecuteAsync("owner-a", id, new SaveTransaction(1, template), token)).IsT0.ShouldBeTrue();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        pause.Arm();
        var posting = database.Store.PostRecurringAsync("owner-a", id, today, cancellation.Token);
        try
        {
            await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20), token);
            await cancellation.CancelAsync();
            await Should.ThrowAsync<OperationCanceledException>(() => posting);
        }
        finally
        {
            await cancellation.CancelAsync();
            try { await posting; }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { /* The cancelled attempt has released its context and transaction. */ }
        }

        // An independent command must acquire the released row and retain the
        // original revision; the cancelled worker must not consume a receipt.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        (await database.Store.ExecuteAsync("owner-a", id, new UpdatePlan(2, "After cancellation", ""), deadline.Token)).IsT0.ShouldBeTrue();
        var unchanged = (await database.Store.ReadAsync("owner-a", id, token)).ShouldNotBeNull();
        unchanged.Transactions.Count.ShouldBe(129);
        unchanged.Version.ShouldBe(3);
        (await database.Store.PostRecurringAsync("owner-a", id, today, token)).ShouldBeTrue();
        var complete = (await database.Store.ReadAsync("owner-a", id, token)).ShouldNotBeNull();
        complete.Transactions.Count.ShouldBe(201);
        complete.Version.ShouldBe(4);
        BudgetFacts.Balance(complete, account, today).Working.ShouldBe(800);
        await using var context = await database.Factory.CreateDbContextAsync(token);
        (await context.Set<BudgetReceipt>().CountAsync(item => item.PlanId == id, token)).ShouldBe(4);
    }

    [Fact]
    public async Task CompetingBackgroundBatchesFinishExactlyOnceWithoutEmptyRevisionsOrUserRebasingAsync()
    {
        var token = TestContext.Current.CancellationToken;
        await using var database = await BudgetDatabase.CreateAsync(token);
        var id = await database.Store.CreateAsync("owner-a", new("Background claims", StarterCategories: false), token);
        var today = (await database.Store.ReadAsync("owner-a", id, token)).ShouldNotBeNull().Today;
        var account = new AccountData(Guid.NewGuid(), "Tracking", AccountKind.Asset, 1000, today.AddDays(-199), false, "");
        (await database.Store.ExecuteAsync("owner-a", id, new SaveAccount(0, account), token)).IsT0.ShouldBeTrue();
        var template = new TransactionData(Guid.NewGuid(), account.Id, account.OpenedOn, "Daily", "", -1, null,
            ClearingState.Uncleared, ClearingState.Uncleared, false, "", [], RepeatFrequency.Daily);
        (await database.Store.ExecuteAsync("owner-a", id, new SaveTransaction(1, template), token)).IsT0.ShouldBeTrue();

        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => database.Store.PostRecurringAsync("owner-a", id, today, token)));

        results.Count(item => item).ShouldBe(1);
        var final = (await database.Store.ReadAsync("owner-a", id, token)).ShouldNotBeNull();
        final.Version.ShouldBe(3);
        final.Transactions.Count.ShouldBe(201);
        final.Transactions.Where(item => item.SourceTemplateId == template.Id).Select(item => item.ScheduledDate).Order()
            .ShouldBe(Enumerable.Range(0, 200).Select(day => (DateOnly?)account.OpenedOn.AddDays(day)).ToArray());
        final.Transactions.Select(item => item.Sequence).Distinct().Count().ShouldBe(201);
        BudgetFacts.Balance(final, account, today).Working.ShouldBe(800);
        final.Changes.Count.ShouldBe(2);
        (await database.Store.ExecuteAsync("owner-a", id, new UpdatePlan(2, "Stale user edit", ""), token)).IsT3.ShouldBeTrue();
        (await database.Store.ExecuteAsync("owner-a", id, new PostRecurring(2, today), token)).IsT3.ShouldBeTrue();
        (await database.Store.PostRecurringAsync("owner-a", id, today, token)).ShouldBeFalse();
        (await database.Store.PostRecurringAsync("owner-b", id, today, token)).ShouldBeFalse();
        (await database.Store.PostRecurringAsync("owner-a", Guid.NewGuid(), today, token)).ShouldBeFalse();
        await using var context = await database.Factory.CreateDbContextAsync(token);
        (await context.Set<BudgetReceipt>().CountAsync(item => item.PlanId == id, token)).ShouldBe(3);
        var saved = await context.Set<BudgetPlan>().SingleAsync(item => item.Id == id, token);
        saved.Version.ShouldBe(3);
        saved.Name.ShouldBe("Background claims");
    }

    private sealed class PausedClaim : SaveChangesInterceptor
    {
        private int _armed;
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Arm() => Interlocked.Exchange(ref _armed, 1);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _armed, 0) == 1)
            {
                Entered.SetResult();
                await _release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
