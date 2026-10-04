using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;
using Xunit;
using YHAB.Features.Budgeting.Data;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.IntegrationTests;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class HistoryAtomicityTests
{
    [Fact]
    public async Task FailureAfterRevisionClaimRollsBackLedgerHistoryAndReceiptAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var token = timeout.Token;
        var fault = new WriteFault();
        await using var database = await BudgetDatabase.CreateAsync(token, interceptor: fault);
        var id = await database.Store.CreateAsync("owner-a", new("Atomic"), token);
        var original = (await database.Store.ReadAsync("owner-a", id, token)).ShouldNotBeNull();
        var command = new AssignMoney(original.Version, original.Categories[0].Id, BudgetFacts.Month(original.Today), 10);
        fault.Remaining = 2;
        (await Should.ThrowAsync<InvalidOperationException>(() => database.Store.ExecuteAsync("owner-a", id, command, token))).Message.ShouldBe("Injected write failure");
        var unchanged = (await database.Store.ReadAsync("owner-a", id, token)).ShouldNotBeNull();
        unchanged.Version.ShouldBe(original.Version);
        unchanged.Allocations.ShouldBeEmpty();
        unchanged.CanUndo.ShouldBeFalse();
        await using var context = await database.Factory.CreateDbContextAsync(token);
        (await context.Set<BudgetReceipt>().CountAsync(token)).ShouldBe(0);
        (await context.Set<BudgetCheckpointInvalidation>().CountAsync(token)).ShouldBe(0);
        (await database.Store.ExecuteAsync("owner-a", id, command, token)).IsT0.ShouldBeTrue();
        var saved = (await database.Store.ReadAsync("owner-a", id, token)).ShouldNotBeNull();
        saved.Allocations.Single().Amount.ShouldBe(10);
        saved.Version.ShouldBe(original.Version + 1);
        (await context.Set<BudgetCheckpointInvalidation>().SingleAsync(token)).Version.ShouldBe(saved.Version);
    }

    [Fact]
    public async Task ConcurrentClaimsAfterTheSameReadCommitExactlyOneChangeAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var token = timeout.Token;
        var barrier = new ClaimBarrier();
        await using var database = await BudgetDatabase.CreateAsync(token, interceptor: barrier);
        var id = await database.Store.CreateAsync("owner-a", new("Concurrent"), token);
        barrier.Armed = true;
        var results = await Task.WhenAll(database.Store.ExecuteAsync("owner-a", id, new UpdatePlan(0, "First", ""), token),
            database.Store.ExecuteAsync("owner-a", id, new UpdatePlan(0, "Second", ""), token));
        results.Count(item => item.IsT0).ShouldBe(1);
        results.Count(item => item.IsT3).ShouldBe(1);
        var saved = (await database.Store.ReadAsync("owner-a", id, token)).ShouldNotBeNull();
        saved.Version.ShouldBe(1);
        saved.Changes.Count.ShouldBe(1);
        await using var context = await database.Factory.CreateDbContextAsync(token);
        (await context.Set<BudgetReceipt>().CountAsync(token)).ShouldBe(1);
    }

    [Fact]
    public async Task RetentionEmptyHistoryAndBranchingUseOnlyMeaningfulUserChangesAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var token = timeout.Token;
        await using var database = await BudgetDatabase.CreateAsync(token);
        var id = await database.Store.CreateAsync("owner-a", new("History"), token);
        var plan = (await database.Store.ReadAsync("owner-a", id, token)).ShouldNotBeNull();
        (await database.Store.ExecuteAsync("owner-a", id, new UndoChange(plan.Version), token)).IsT1.ShouldBeTrue();
        (await database.Store.ExecuteAsync("owner-a", id, new RedoChange(plan.Version), token)).IsT1.ShouldBeTrue();
        for (var index = 1; index <= 52; index++)
        {
            (await database.Store.ExecuteAsync("owner-a", id, new AssignMoney(plan.Version, plan.Categories[0].Id, BudgetFacts.Month(plan.Today), index), token)).IsT0.ShouldBeTrue();
            plan = (await database.Store.ReadAsync("owner-a", id, token)).ShouldNotBeNull();
        }
        await using var context = await database.Factory.CreateDbContextAsync(token);
        (await context.Set<BudgetHistory>().CountAsync(token)).ShouldBe(50);
        (await context.Set<BudgetCheckpointInvalidation>().SingleAsync(token)).Version.ShouldBe(52);
        (await database.Store.ExecuteAsync("owner-a", id, new AssignMoney(plan.Version, plan.Categories[0].Id, BudgetFacts.Month(plan.Today), 52), token)).IsT0.ShouldBeTrue();
        (await context.Set<BudgetHistory>().CountAsync(token)).ShouldBe(50);
        plan = (await database.Store.ReadAsync("owner-a", id, token)).ShouldNotBeNull();
        (await database.Store.ExecuteAsync("owner-a", id, new UndoChange(plan.Version), token)).IsT0.ShouldBeTrue();
        plan = (await database.Store.ReadAsync("owner-a", id, token)).ShouldNotBeNull();
        plan.Allocations.Single().Amount.ShouldBe(51);
        plan.CanRedo.ShouldBeTrue();
        (await database.Store.ExecuteAsync("owner-a", id, new AssignMoney(plan.Version, plan.Categories[0].Id, BudgetFacts.Month(plan.Today), 99), token)).IsT0.ShouldBeTrue();
        (await database.Store.ReadAsync("owner-a", id, token)).ShouldNotBeNull().CanRedo.ShouldBeFalse();
    }

    [Fact]
    public async Task ExhaustingRetainedHistoryRejectsAnotherUndoWithoutWritingAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var token = timeout.Token;
        await using var database = await BudgetDatabase.CreateAsync(token);
        var id = await database.Store.CreateAsync("owner-a", new("Capacity"), token);
        var plan = (await database.Store.ReadAsync("owner-a", id, token)).ShouldNotBeNull();
        for (var amount = 1; amount <= 52; amount++)
        {
            var saved = await database.Store.ExecuteAsync("owner-a", id, new AssignMoney(plan.Version, plan.Categories[0].Id, BudgetFacts.Month(plan.Today), amount), token);
            plan = saved.AsT0;
        }
        for (var remaining = 49; remaining >= 0; remaining--)
        {
            (await database.Store.ExecuteAsync("owner-a", id, new UndoChange(plan.Version), token)).IsT0.ShouldBeTrue();
            plan = (await database.Store.ReadAsync("owner-a", id, token)).ShouldNotBeNull();
            plan.Allocations.Single().Amount.ShouldBe(remaining + 2);
            plan.CanUndo.ShouldBe(remaining > 0);
            plan.CanRedo.ShouldBeTrue();
        }
        var original = System.Text.Json.JsonSerializer.Serialize(plan);
        var rejected = await database.Store.ExecuteAsync("owner-a", id, new UndoChange(plan.Version), token);
        rejected.IsT1.ShouldBeTrue();
        System.Text.Json.JsonSerializer.Serialize(await database.Store.ReadAsync("owner-a", id, token)).ShouldBe(original);
        await using var context = await database.Factory.CreateDbContextAsync(token);
        (await context.Set<BudgetPlan>().SingleAsync(token)).HistoryCursor.ShouldBe(2);
        (await context.Set<BudgetHistory>().CountAsync(token)).ShouldBe(50);
        (await context.Set<BudgetReceipt>().CountAsync(token)).ShouldBe(102);
    }

    [Fact]
    public async Task UndoAndEditAfterTheSameReadHaveExactlyOneWinnerAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var token = timeout.Token;
        var barrier = new ClaimBarrier { Version = 1 };
        await using var database = await BudgetDatabase.CreateAsync(token, interceptor: barrier);
        var id = await database.Store.CreateAsync("owner-a", new("Original"), token);
        (await database.Store.ExecuteAsync("owner-a", id, new UpdatePlan(0, "First", ""), token)).IsT0.ShouldBeTrue();
        barrier.Armed = true;
        var results = await Task.WhenAll(database.Store.ExecuteAsync("owner-a", id, new UndoChange(1), token),
            database.Store.ExecuteAsync("owner-a", id, new UpdatePlan(1, "Second", ""), token));
        results.Count(item => item.IsT0).ShouldBe(1);
        results.Count(item => item.IsT3).ShouldBe(1);
        var undoWon = results[0].IsT0;
        var saved = (await database.Store.ReadAsync("owner-a", id, token)).ShouldNotBeNull();
        saved.Version.ShouldBe(2);
        saved.Name.ShouldBe(undoWon ? "Original" : "Second");
        saved.CanRedo.ShouldBe(undoWon);
        await using var context = await database.Factory.CreateDbContextAsync(token);
        (await context.Set<BudgetPlan>().SingleAsync(token)).HistoryCursor.ShouldBe(undoWon ? 0 : 2);
        (await context.Set<BudgetHistory>().CountAsync(token)).ShouldBe(undoWon ? 1 : 2);
        (await context.Set<BudgetReceipt>().CountAsync(token)).ShouldBe(2);
    }

    [Fact]
    public async Task FailedUndoRollsBackCursorLedgerRevisionHistoryAndReceiptAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var token = timeout.Token;
        var fault = new WriteFault();
        await using var database = await BudgetDatabase.CreateAsync(token, interceptor: fault);
        var id = await database.Store.CreateAsync("owner-a", new("Rollback undo"), token);
        var initial = (await database.Store.ReadAsync("owner-a", id, token)).ShouldNotBeNull();
        (await database.Store.ExecuteAsync("owner-a", id, new AssignMoney(0, initial.Categories[0].Id, BudgetFacts.Month(initial.Today), 25), token)).IsT0.ShouldBeTrue();
        await database.Queries.MonthAsync("owner-a", id, BudgetFacts.Month(initial.Today), 1, token);
        var before = System.Text.Json.JsonSerializer.Serialize(await database.Store.ReadAsync("owner-a", id, token));
        var command = new UndoChange(1);
        fault.Remaining = 2;
        (await Should.ThrowAsync<InvalidOperationException>(() => database.Store.ExecuteAsync("owner-a", id, command, token))).Message.ShouldBe("Injected write failure");
        System.Text.Json.JsonSerializer.Serialize(await database.Store.ReadAsync("owner-a", id, token)).ShouldBe(before);
        await using var context = await database.Factory.CreateDbContextAsync(token);
        (await context.Set<BudgetPlan>().SingleAsync(token)).HistoryCursor.ShouldBe(1);
        (await context.Set<BudgetHistory>().CountAsync(token)).ShouldBe(1);
        (await context.Set<BudgetReceipt>().CountAsync(token)).ShouldBe(1);
        (await context.Set<BudgetCheckpoint>().CountAsync(token)).ShouldBe(2);
        (await context.Set<BudgetCheckpointInvalidation>().AsNoTracking().SingleAsync(token)).Version.ShouldBe(1);
        (await database.Store.ExecuteAsync("owner-a", id, command, token)).IsT0.ShouldBeTrue();
        var restored = (await database.Store.ReadAsync("owner-a", id, token)).ShouldNotBeNull();
        restored.Allocations.ShouldBeEmpty();
        restored.Version.ShouldBe(2);
        restored.CanRedo.ShouldBeTrue();
        restored.CanUndo.ShouldBeFalse();
        (await context.Set<BudgetCheckpoint>().CountAsync(token)).ShouldBe(1);
        (await context.Set<BudgetCheckpointInvalidation>().AsNoTracking().SingleAsync(token)).Version.ShouldBe(2);
    }

    private sealed class WriteFault : SaveChangesInterceptor
    {
        public int Remaining { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Remaining > 0 && --Remaining == 0) { throw new InvalidOperationException("Injected write failure"); }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ClaimBarrier : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource _bothReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;
        public bool Armed { get; set; }
        public long Version { get; set; }
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<BudgetPlan>().Any(entry => entry.Property(item => item.Version).OriginalValue == Version && entry.Entity.Version == Version + 1))
            {
                if (Interlocked.Increment(ref _arrivals) == 2) { _bothReady.TrySetResult(); }
                await _bothReady.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
