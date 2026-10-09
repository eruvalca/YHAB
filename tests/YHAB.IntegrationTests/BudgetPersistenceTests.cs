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
    public async Task ReorderingPersistsAtomicallyAndSupportsUndoRedoAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        await using var database = await BudgetDatabase.CreateAsync(timeout.Token);
        var store = database.Store;
        var id = await store.CreateAsync("owner-a", new("Ordering"), timeout.Token);
        var plan = (await store.ReadAsync("owner-a", id, timeout.Token)).ShouldNotBeNull();
        var original = plan;
        var category = plan.Categories[0];
        var destination = plan.Groups.First(item => item.Id != category.GroupId);
        var anchor = plan.Categories.First(item => item.GroupId == destination.Id);
        var command = new ReorderCategory(plan.Version, category.Id, destination.Id, anchor.Id);
        plan = await ApplyAsync(store, plan, command, timeout.Token);
        var ordered = plan.Categories.Where(item => item.GroupId == destination.Id).OrderBy(item => item.SortOrder).ToArray();
        ordered.Take(2).Select(item => item.Id).ShouldBe([category.Id, anchor.Id]);
        plan.Categories.Single(item => item.Id == category.Id).ShouldBe(category with { GroupId = destination.Id, SortOrder = 0 });
        plan.Version.ShouldBe(original.Version + 1);
        (await store.ExecuteAsync("owner-a", id, new ReorderGroup(original.Version, destination.Id, null), timeout.Token)).IsT3.ShouldBeTrue();
        plan = await ApplyAsync(store, plan, new UndoChange(plan.Version), timeout.Token);
        plan.Categories.OrderBy(item => item.Id).ShouldBe(original.Categories.OrderBy(item => item.Id));
        plan = await ApplyAsync(store, plan, new RedoChange(plan.Version), timeout.Token);
        plan.Categories.Single(item => item.Id == category.Id).GroupId.ShouldBe(destination.Id);
        plan = await ApplyAsync(store, plan, new ReorderGroup(plan.Version, destination.Id, plan.Groups.OrderBy(item => item.SortOrder).First().Id), timeout.Token);
        plan.Groups.OrderBy(item => item.SortOrder).First().Id.ShouldBe(destination.Id);
        plan = await ApplyAsync(store, plan, new UndoChange(plan.Version), timeout.Token);
        plan.Groups.OrderBy(item => item.Id).ShouldBe(original.Groups.OrderBy(item => item.Id));
        plan.Allocations.ShouldBe(original.Allocations);
        plan.Transactions.ShouldBe(original.Transactions);
    }

    [Fact]
    public async Task MergingAssignedCategoryPersistsHistoryAndSupportsUndoRedoAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        await using var database = await BudgetDatabase.CreateAsync(timeout.Token);
        var store = database.Store;
        var id = await store.CreateAsync("owner-a", new("Merge regression"), timeout.Token);
        var plan = (await store.ReadAsync("owner-a", id, timeout.Token)).ShouldNotBeNull();
        var source = plan.Categories[0].Id;
        var destination = plan.Categories[1].Id;
        var account = new AccountData(Guid.NewGuid(), "Checking", AccountKind.Checking, 1000, plan.Today, false, "");
        plan = await ApplyAsync(store, plan, new SaveAccount(plan.Version, account), timeout.Token);
        plan = await ApplyAsync(store, plan, new AssignMoney(plan.Version, source, BudgetFacts.Month(plan.Today), 100), timeout.Token);
        plan = await ApplyAsync(store, plan, new AssignMoney(plan.Version, destination, BudgetFacts.Month(plan.Today), 50), timeout.Token);
        var entry = new TransactionData(Guid.NewGuid(), account.Id, plan.Today, "Market", "Preserve memo", -25, null,
            ClearingState.Cleared, ClearingState.Uncleared, false, "", [new(Guid.NewGuid(), source, -25, "Preserve split")]);
        plan = await ApplyAsync(store, plan, new SaveTransaction(plan.Version, entry), timeout.Token);

        plan = await ApplyAsync(store, plan, new RemoveCategory(plan.Version, source, destination), timeout.Token);

        plan.Categories.ShouldNotContain(item => item.Id == source);
        plan.Allocations.Single().CategoryId.ShouldBe(destination);
        plan.Allocations.Single().Amount.ShouldBe(150);
        plan.Transactions.Single().Splits.Single().CategoryId.ShouldBe(destination);
        BudgetCalculator.Calculate(plan, BudgetFacts.Month(plan.Today), plan.Today).Categories.Single(item => item.Category.Id == destination).Available.ShouldBe(125);
        plan = await ApplyAsync(store, plan, new UndoChange(plan.Version), timeout.Token);
        plan.Categories.ShouldContain(item => item.Id == source);
        plan.Allocations.Single(item => item.CategoryId == source).Amount.ShouldBe(100);
        plan.Allocations.Single(item => item.CategoryId == destination).Amount.ShouldBe(50);
        plan.Transactions.Single().Splits.Single().CategoryId.ShouldBe(source);
        plan = await ApplyAsync(store, plan, new RedoChange(plan.Version), timeout.Token);
        plan.Categories.ShouldNotContain(item => item.Id == source);
        plan.Allocations.Single().Amount.ShouldBe(150);
        plan.Transactions.Single().Splits.Single().ShouldBe(entry.Splits[0] with { CategoryId = destination });
        plan.Transactions.Single().Memo.ShouldBe(entry.Memo);
        BudgetFacts.Balance(plan, account, plan.Today).Working.ShouldBe(975);
    }

    private static async Task<PlanSnapshot> ApplyAsync(BudgetStore store, PlanSnapshot plan, PlanCommand command, CancellationToken token)
    {
        (await store.ExecuteAsync("owner-a", plan.Id, command, token)).IsT0.ShouldBeTrue();
        return (await store.ReadAsync("owner-a", plan.Id, token)).ShouldNotBeNull();
    }

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
