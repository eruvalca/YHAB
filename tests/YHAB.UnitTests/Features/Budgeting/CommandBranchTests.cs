using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Shouldly;
using Xunit;
using YHAB.Features.Budgeting.Services;
using YHAB.SharedKernel.Budgeting;
using static YHAB.UnitTests.Features.Budgeting.BudgetTestData;

namespace YHAB.UnitTests.Features.Budgeting;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class CommandBranchTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpeningDateIgnoresUnrelatedEntriesButChecksBothTransferSides(bool destination)
    {
        var plan = Create();
        var third = plan.Accounts[0] with { Id = Guid.NewGuid(), Name = "Savings" };
        var unrelated = Entry(plan, 1, -5, date: January) with { TransferAccountId = third.Id, Splits = [] };
        var own = Entry(plan, destination ? 1 : 0, -10, date: January.AddDays(10)) with
        {
            TransferAccountId = destination ? plan.Accounts[0].Id : third.Id,
            Splits = [],
        };
        plan = plan with { Accounts = [.. plan.Accounts, third], Transactions = [Entry(plan, 1, -2, date: January), unrelated, own] };
        var original = JsonSerializer.Serialize(plan);
        var allowed = CatalogChanges.Save(NewIds(), plan, new SaveAccount(0, plan.Accounts[0] with { OpenedOn = own.Date }), plan.Today).AsT0;
        allowed.Accounts.Single(item => item.Id == plan.Accounts[0].Id).OpenedOn.ShouldBe(own.Date);
        allowed.Transactions.ShouldBe(plan.Transactions);
        var rejected = CatalogChanges.Save(NewIds(), plan, new SaveAccount(0, plan.Accounts[0] with { OpenedOn = own.Date.AddDays(1) }), plan.Today);
        rejected.AsT1.Message.ShouldBe("The opening date must be on or before this account's first transaction.");
        JsonSerializer.Serialize(plan).ShouldBe(original);
    }

    [Fact]
    public void UsedCategoryNeedsSpendingReplacementAndPreservesUnassignedIncome()
    {
        var plan = Create();
        var replacement = plan.Categories[0] with { Id = Guid.NewGuid(), Name = "Other" };
        var purchase = Entry(plan, 0, -10);
        var income = Entry(plan, 0, 100, category: -1);
        plan = plan with { Categories = [.. plan.Categories, replacement], Transactions = [income, purchase] };
        var original = JsonSerializer.Serialize(plan);
        foreach (var destination in new Guid?[] { null, plan.Categories[1].Id })
        {
            CatalogChanges.Remove(plan, new(0, plan.Categories[0].Id, destination)).AsT1.Message
                .ShouldBe("Choose another category to receive this category's history and assigned money.");
        }
        var merged = CatalogChanges.Remove(plan, new(0, plan.Categories[0].Id, replacement.Id)).AsT0;
        JsonSerializer.Serialize(merged.Transactions[0]).ShouldBe(JsonSerializer.Serialize(income));
        merged.Transactions[1].Splits.Single().ShouldBe(purchase.Splits[0] with { CategoryId = replacement.Id });
        merged.Categories.Select(item => item.Id).ShouldBe([plan.Categories[1].Id, replacement.Id]);
        JsonSerializer.Serialize(plan).ShouldBe(original);
    }

    [Fact]
    public void PaymentCategoryCannotBeUsedAsASpendingSplit()
    {
        var plan = Create();
        var original = JsonSerializer.Serialize(plan);
        var entry = Entry(plan, 0, -10, category: 1);
        TransactionChanges.Save(NewIds(), plan, new(0, entry), plan.Today).AsT1.Message
            .ShouldBe("Choose spending categories for outflows, valid split amounts, and split memos of at most 1,000 characters.");
        JsonSerializer.Serialize(plan).ShouldBe(original);
    }

    [Fact]
    public void UnassignedInflowCanBeSavedButCannotBeClearedFromAnotherAccount()
    {
        var plan = Create();
        var income = Entry(plan, 0, 100, category: -1);
        var saved = TransactionChanges.Save(NewIds(), plan, new(0, income), plan.Today).AsT0;
        JsonSerializer.Serialize(saved.Transactions.Single()).ShouldBe(JsonSerializer.Serialize(income));
        var original = JsonSerializer.Serialize(saved);
        TransactionChanges.UpdateStates(saved, new(0, [income.Id], ClearingState.Cleared, false, plan.Accounts[1].Id)).AsT1.Message
            .ShouldBe("Select transactions and a valid clearing state. Reconciled status is set through Reconcile.");
        JsonSerializer.Serialize(saved).ShouldBe(original);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ScopedAndGlobalApprovalPreserveUnselectedEntriesAndClearingSides(bool scoped, bool transfer)
    {
        var plan = Create();
        var unrelated = Entry(plan, 1, -20);
        var selected = Entry(plan, 0, -10) with
        {
            TransferAccountId = transfer ? plan.Accounts[1].Id : null,
            TransferState = ClearingState.Cleared,
            NeedsApproval = true,
        };
        plan = plan with { Transactions = [unrelated, selected] };
        var original = JsonSerializer.Serialize(plan);
        var perspective = transfer ? plan.Accounts[1].Id : plan.Accounts[0].Id;
        var account = scoped ? perspective : (Guid?)null;
        var result = TransactionChanges.UpdateStates(plan, new(0, [selected.Id], null, true, account)).AsT0;
        result.Transactions[0].ShouldBe(unrelated);
        result.Transactions[1].ShouldBe(selected with { NeedsApproval = false });
        JsonSerializer.Serialize(plan).ShouldBe(original);
    }
}
