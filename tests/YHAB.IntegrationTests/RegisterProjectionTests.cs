using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;
using YHAB.Features.Budgeting.Data;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.IntegrationTests;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class RegisterProjectionTests
{
    [Fact]
    public async Task PagesFiltersAndTransferPerspectivesRetainExactFinancialOrderAsync()
    {
        var token = TestContext.Current.CancellationToken;
        await using var database = await BudgetDatabase.CreateAsync(token);
        var plan = await SeedAsync(database, token);
        var checking = plan.Accounts.Single(item => string.Equals(item.Name, "Checking", StringComparison.Ordinal));
        var savings = plan.Accounts.Single(item => string.Equals(item.Name, "Savings", StringComparison.Ordinal));
        var expected = new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            ["salary"] = 1100,
            ["first food"] = 1070,
            ["transfer"] = 870,
            ["gift"] = 885,
            ["tag\\path"] = 860,
            ["last food"] = 830,
        };
        foreach (var sort in new[] { "Newest first", "Oldest first", "Payee", "Amount" })
        {
            var rows = new List<RegisterRow>();
            RegisterCursor? cursor = null;
            do
            {
                var page = await database.Queries.RegisterAsync("owner-a", plan.Id,
                    new(checking.Id, Sort: sort, PageSize: 2, After: cursor, Version: plan.Version), token);
                page.Total.ShouldBe(6);
                rows.AddRange(page.Rows);
                cursor = page.Next;
            } while (cursor is not null);
            rows.Count.ShouldBe(6);
            rows.Select(item => item.Transaction.Id).Distinct().Count().ShouldBe(6);
            foreach (var row in rows) { row.RunningBalance.ShouldBe(expected[row.Transaction.Memo]); }
            if (string.Equals(sort, "Oldest first", StringComparison.Ordinal)) { rows.Select(item => item.Transaction.Memo).ShouldBe(expected.Keys); }
            if (string.Equals(sort, "Newest first", StringComparison.Ordinal)) { rows.Select(item => item.Transaction.Memo).ShouldBe(expected.Keys.Reverse()); }
            if (string.Equals(sort, "Amount", StringComparison.Ordinal)) { rows.Select(item => item.Transaction.Amount).ShouldBe([-200m, -30m, -30m, -25m, 15m, 100m]); }
        }
        var destination = await database.Queries.RegisterAsync("owner-a", plan.Id, new(savings.Id, Filter: "Cleared"), token);
        destination.Rows.Single().RunningBalance.ShouldBe(400);
        destination.Rows.Single().Transaction.Memo.ShouldBe("transfer");
        (await database.Queries.RegisterAsync("owner-a", plan.Id, new(checking.Id, Filter: "Reconciled"), token)).Rows.Single().Transaction.Memo.ShouldBe("transfer");
        (await database.Queries.RegisterAsync("owner-a", plan.Id, new(checking.Id, Filter: "Needs approval"), token)).Rows.Single().Transaction.Memo.ShouldBe("gift");
        (await database.Queries.RegisterAsync("owner-a", plan.Id, new(checking.Id, Filter: "Uncleared"), token)).Total.ShouldBe(2);
        (await database.Queries.RegisterAsync("owner-a", plan.Id, new(checking.Id, Filter: "Cleared"), token)).Total.ShouldBe(3);
        (await database.Queries.RegisterAsync("owner-a", plan.Id, new(checking.Id, Filter: "Recurring"), token)).Total.ShouldBe(1);
        var middle = plan.Today.AddMonths(-1).AddDays(1);
        (await database.Queries.RegisterAsync("owner-a", plan.Id, new(checking.Id, From: middle, Through: middle), token)).Total.ShouldBe(3);
        foreach (var (search, count) in new[] { ("%", 1), ("_", 1), ("\\", 1), ("Groceries", 3), ("Ready to assign", 2), ("Transfer", 1), ("No payee", 1), ("Blue", 1), ("-15-15", 2) })
        {
            (await database.Queries.RegisterAsync("owner-a", plan.Id, new(checking.Id, Search: search), token)).Total.ShouldBe(count, search);
        }
        var view = await database.Queries.ViewAsync("owner-a", plan.Id, token);
        view.Balances.Single(item => item.AccountId == checking.Id).Working.ShouldBe(830);
        view.Balances.Single(item => item.AccountId == savings.Id).Working.ShouldBe(400);
        (await Should.ThrowAsync<BudgetRequestException>(() => database.Queries.RegisterAsync("owner-b", plan.Id, new(), token))).Status.ShouldBe(404);
        (await Should.ThrowAsync<BudgetRequestException>(() => database.Queries.RegisterAsync("owner-a", plan.Id, new(Guid.NewGuid()), token))).Status.ShouldBe(404);
    }

    [Fact]
    public async Task CachedMonthsAreOwnerScopedAndInvalidatedByBackdatedChangesAsync()
    {
        var token = TestContext.Current.CancellationToken;
        await using var database = await BudgetDatabase.CreateAsync(token);
        var plan = await SeedAsync(database, token);
        var month = BudgetFacts.Month(plan.Today);
        var original = await database.Queries.MonthAsync("owner-a", plan.Id, month, plan.Version, token);
        (await database.Queries.MonthAsync("owner-a", plan.Id, month, plan.Version, token)).ShouldBeSameAs(original);
        (await Should.ThrowAsync<BudgetRequestException>(() => database.Queries.MonthAsync("owner-b", plan.Id, month, plan.Version, token))).Status.ShouldBe(404);
        var salary = plan.Transactions.Single(item => string.Equals(item.Memo, "salary", StringComparison.Ordinal));
        (await database.Store.ExecuteAsync("owner-a", plan.Id, new SaveTransaction(plan.Version, salary with
        {
            Amount = 125,
            Splits = [salary.Splits[0] with { Amount = 125 }],
        }), token)).IsT0.ShouldBeTrue();
        var latest = (await database.Store.ReadAsync("owner-a", plan.Id, token)).ShouldNotBeNull();
        (await Should.ThrowAsync<BudgetRequestException>(() => database.Queries.MonthAsync("owner-a", plan.Id, month, plan.Version, token))).Status.ShouldBe(409);
        (await Should.ThrowAsync<BudgetRequestException>(() => database.Queries.RegisterAsync("owner-a", plan.Id, new(Version: plan.Version), token))).Status.ShouldBe(409);
        (await database.Queries.RevisionAsync("owner-a", plan.Id, token)).ShouldBe(latest.Version);
        var revised = await database.Queries.MonthAsync("owner-a", plan.Id, month, latest.Version, token);
        revised.ReadyToAssign.ShouldBe(original.ReadyToAssign + 25);
        revised.ReadyToAssign.ShouldBe(BudgetCalculator.Calculate(latest, month, latest.Today).ReadyToAssign);
    }

    private static async Task<PlanSnapshot> SeedAsync(BudgetDatabase database, CancellationToken token)
    {
        var id = await database.Store.CreateAsync("owner-a", new("Register checks"), token);
        var plan = (await database.Store.ReadAsync("owner-a", id, token)).ShouldNotBeNull();
        var day = plan.Today.AddMonths(-1);
        var checking = new AccountData(Guid.NewGuid(), "Checking", AccountKind.Checking, 1000, day, false, "");
        var savings = checking with { Id = Guid.NewGuid(), Name = "Savings", OpeningBalance = 200 };
        foreach (var account in new[] { checking, savings })
        {
            (await database.Store.ExecuteAsync("owner-a", id, new SaveAccount(plan.Version, account), token)).IsT0.ShouldBeTrue();
            plan = (await database.Store.ReadAsync("owner-a", id, token)).ShouldNotBeNull();
        }
        var groceries = plan.Categories.Single(item => string.Equals(item.Name, "Groceries", StringComparison.Ordinal)).Id;
        TransactionData[] entries =
        [
            Entry(checking.Id, day, "Work", "salary", 100, null, ClearingState.Cleared),
            Entry(checking.Id, day.AddDays(1), "Market", "first food", -30, groceries, ClearingState.Uncleared),
            Entry(checking.Id, day.AddDays(1), "", "transfer", -200, null, ClearingState.Cleared) with { TransferAccountId = savings.Id, TransferState = ClearingState.Cleared, Splits = [] },
            Entry(checking.Id, day.AddDays(1), "", "gift", 15, null, ClearingState.Cleared) with { NeedsApproval = true },
            Entry(checking.Id, day.AddDays(2), "A_100% Market", "tag\\path", -25, groceries, ClearingState.Uncleared) with { Flag = "Blue" },
            Entry(checking.Id, day.AddDays(2), "Market", "last food", -30, groceries, ClearingState.Cleared),
            Entry(checking.Id, plan.Today.AddDays(1), "Future", "template", -10, groceries, ClearingState.Uncleared) with { Repeat = RepeatFrequency.Monthly },
        ];
        foreach (var entry in entries)
        {
            (await database.Store.ExecuteAsync("owner-a", id, new SaveTransaction(plan.Version, entry), token)).IsT0.ShouldBeTrue();
            plan = (await database.Store.ReadAsync("owner-a", id, token)).ShouldNotBeNull();
        }
        await using var context = await database.Factory.CreateDbContextAsync(token);
        var transfer = await context.Set<BudgetTransaction>().SingleAsync(item => item.PlanId == id && item.Memo == "transfer", token);
        transfer.State = ClearingState.Reconciled;
        await context.SaveChangesAsync(token);
        return (await database.Store.ReadAsync("owner-a", id, token)).ShouldNotBeNull();
    }

    private static TransactionData Entry(Guid account, DateOnly date, string payee, string memo, decimal amount, Guid? category, ClearingState state)
        => new(Guid.Empty, account, date, payee, memo, amount, null, state, ClearingState.Uncleared, false, "", [new(Guid.Empty, category, amount, "")]);
}
