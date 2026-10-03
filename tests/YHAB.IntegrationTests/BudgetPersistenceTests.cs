using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using Xunit;
using YHAB.Features.Budgeting.Data;
using YHAB.Features.Budgeting.Services;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.IntegrationTests;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class BudgetPersistenceTests
{
    [Fact]
    public async Task OwnerIsolationHistoryAndConcurrentWritesPersistAcrossContextsAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        await using var database = await BudgetDatabase.CreateAsync(timeout.Token);
        var store = database.Store;
        var id = await store.CreateAsync("owner-a", new("Private plan"), timeout.Token);
        (await store.ListAsync("owner-b", timeout.Token)).ShouldBeEmpty();
        (await store.ReadAsync("owner-b", id, timeout.Token)).ShouldBeNull();
        (await store.ExecuteAsync("owner-b", id, new UpdatePlan(0, "Stolen", ""), timeout.Token)).IsT2.ShouldBeTrue();
        var plan = (await store.ReadAsync("owner-a", id, timeout.Token)).ShouldNotBeNull();
        plan.Categories.Count.ShouldBe(14);
        var account = new AccountData(Guid.NewGuid(), "Checking", AccountKind.Checking, 1000, plan.Today, false, "");
        (await store.ExecuteAsync("owner-a", id, new SaveAccount(plan.Version, account), timeout.Token)).IsT0.ShouldBeTrue();
        plan = (await store.ReadAsync("owner-a", id, timeout.Token)).ShouldNotBeNull();
        plan.Accounts.Single().Name.ShouldBe("Checking");
        plan.Version.ShouldBe(1);
        var category = plan.Categories[0].Id;
        (await store.ExecuteAsync("owner-a", id, new AssignMoney(plan.Version, category, BudgetFacts.Month(plan.Today), 400), timeout.Token)).IsT0.ShouldBeTrue();
        plan = (await store.ReadAsync("owner-a", id, timeout.Token)).ShouldNotBeNull();
        plan.Allocations.Single().Amount.ShouldBe(400);
        await VerifyHistoryAsync(store, plan, timeout.Token);
        await VerifyConcurrencyAsync(store, id, timeout.Token);
    }

    [Fact]
    public async Task DatabaseRejectsCrossPlanReferencesAndAccountDeletionRemovesOwnedLedgerAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        await using var database = await BudgetDatabase.CreateAsync(timeout.Token);
        var a = await database.Store.CreateAsync("owner-a", new("A"), timeout.Token);
        var b = await database.Store.CreateAsync("owner-b", new("B"), timeout.Token);
        var account = new AccountData(Guid.NewGuid(), "Checking", AccountKind.Checking, 100, new(2026, 10, 2), false, "");
        (await database.Store.ExecuteAsync("owner-a", a, new SaveAccount(0, account), timeout.Token)).IsT0.ShouldBeTrue();
        await using (var context = await database.Factory.CreateDbContextAsync(timeout.Token))
        {
            context.Add(new BudgetTransaction { Id = Guid.NewGuid(), PlanId = b, AccountId = account.Id, Date = new(2026, 10, 2), Payee = "", Memo = "", Flag = "" });
            var exception = await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync(timeout.Token));
            exception.InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
        }
        var plan = (await database.Store.ReadAsync("owner-a", a, timeout.Token)).ShouldNotBeNull();
        var entry = new TransactionData(Guid.NewGuid(), account.Id, plan.Today, "Store", "", -25, null, ClearingState.Cleared, ClearingState.Uncleared, false, "",
            [new(Guid.NewGuid(), plan.Categories[0].Id, -25, "")]);
        (await database.Store.ExecuteAsync("owner-a", a, new SaveTransaction(plan.Version, entry), timeout.Token)).IsT0.ShouldBeTrue();
        await using (var context = await database.Factory.CreateDbContextAsync(timeout.Token))
        {
            context.Users.Remove(await context.Users.SingleAsync(item => item.Id == "owner-a", timeout.Token));
            await context.SaveChangesAsync(timeout.Token);
        }
        (await database.Store.ReadAsync("owner-a", a, timeout.Token)).ShouldBeNull();
        (await database.Store.ReadAsync("owner-b", b, timeout.Token)).ShouldNotBeNull();
        await using var verify = await database.Factory.CreateDbContextAsync(timeout.Token);
        (await verify.Set<BudgetTransaction>().AnyAsync(item => item.PlanId == a, timeout.Token)).ShouldBeFalse();
        (await verify.Set<BudgetHistory>().AnyAsync(item => item.PlanId == a, timeout.Token)).ShouldBeFalse();
    }

    private static async Task VerifyHistoryAsync(BudgetStore store, PlanSnapshot plan, CancellationToken token)
    {
        (await store.ExecuteAsync("owner-a", plan.Id, new UndoChange(plan.Version), token)).IsT0.ShouldBeTrue();
        var undone = (await store.ReadAsync("owner-a", plan.Id, token)).ShouldNotBeNull();
        undone.Allocations.ShouldBeEmpty();
        undone.Accounts.Single().OpeningBalance.ShouldBe(1000);
        undone.CanRedo.ShouldBeTrue();
        (await store.ExecuteAsync("owner-a", plan.Id, new RedoChange(undone.Version), token)).IsT0.ShouldBeTrue();
        var redone = (await store.ReadAsync("owner-a", plan.Id, token)).ShouldNotBeNull();
        redone.Allocations.Single().Amount.ShouldBe(400);
        redone.CanRedo.ShouldBeFalse();
        BudgetCalculator.Calculate(redone, BudgetFacts.Month(redone.Today), redone.Today).ReadyToAssign.ShouldBe(600);
    }

    private static async Task VerifyConcurrencyAsync(BudgetStore store, Guid id, CancellationToken token)
    {
        var plan = (await store.ReadAsync("owner-a", id, token)).ShouldNotBeNull();
        var results = await Task.WhenAll(store.ExecuteAsync("owner-a", id, new UpdatePlan(plan.Version, "One", ""), token),
            store.ExecuteAsync("owner-a", id, new UpdatePlan(plan.Version, "Two", ""), token));
        results.Count(item => item.IsT0).ShouldBe(1);
        results.Count(item => item.IsT3).ShouldBe(1);
        var saved = (await store.ReadAsync("owner-a", id, token)).ShouldNotBeNull();
        saved.Version.ShouldBe(plan.Version + 1);
        saved.Allocations.Single().Amount.ShouldBe(400);
    }
}

