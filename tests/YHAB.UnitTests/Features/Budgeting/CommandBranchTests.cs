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
            .ShouldBe("Choose a spending category from this plan.");
        JsonSerializer.Serialize(plan).ShouldBe(original);
    }

    [Fact]
    public void UncategorizedOutflowNeedsASpendingCategory()
    {
        var plan = Create();
        var original = JsonSerializer.Serialize(plan);
        var entry = Entry(plan, 0, -10) with { Splits = [new(Guid.NewGuid(), null, -10, "")] };
        TransactionChanges.Save(NewIds(), plan, new(0, entry), plan.Today).AsT1.Message
            .ShouldBe("An outflow needs a spending category. Choose one, or switch the direction to Inflow.");
        JsonSerializer.Serialize(plan).ShouldBe(original);
    }

    [Theory]
    [InlineData(10, "Money leaving your budget needs a spending category.")]
    [InlineData(-10, null)]
    public void TrackingSourceTransferUsesBudgetSideCategoryRequirement(decimal amount, string? message)
    {
        var plan = Create();
        var tracking = plan.Accounts[0] with { Kind = AccountKind.Asset };
        plan = plan with { Accounts = [tracking, plan.Accounts[1]] };
        var entry = new TransactionData(Guid.NewGuid(), tracking.Id, plan.Today, "Transfer", "", amount,
            plan.Accounts[1].Id, "", [new(Guid.NewGuid(), null, amount, "")]);
        var result = TransactionChanges.Save(NewIds(), plan, new(0, entry), plan.Today);
        if (message is null)
        {
            result.IsT0.ShouldBeTrue();
            result.AsT0.Transactions.Single().Splits.Single().CategoryId.ShouldBeNull();
        }
        else
        {
            result.AsT1.Message.ShouldBe(message);
        }
    }

    [Fact]
    public void UncategorizedInflowSplitUsesTheBudgetCategoryMessage()
    {
        var plan = Create();
        var entry = Entry(plan, 0, 10) with
        {
            Splits = [new(Guid.NewGuid(), null, -5, ""), new(Guid.NewGuid(), plan.Categories[0].Id, 15, "")],
        };
        TransactionChanges.Save(NewIds(), plan, new(0, entry), plan.Today).AsT1.Message
            .ShouldBe("Money leaving your budget needs a spending category.");
    }

    [Fact]
    public void ZeroAmountTransactionIsRejected()
    {
        var plan = Create();
        var original = JsonSerializer.Serialize(plan);
        var entry = Entry(plan, 0, 0);
        TransactionChanges.Save(NewIds(), plan, new(0, entry), plan.Today).AsT1.Message
            .ShouldBe("Enter a transaction amount other than zero.");
        JsonSerializer.Serialize(plan).ShouldBe(original);
    }

    [Fact]
    public void UnassignedInflowCanBeSaved()
    {
        var plan = Create();
        var income = Entry(plan, 0, 100, category: -1);
        var saved = TransactionChanges.Save(NewIds(), plan, new(0, income), plan.Today).AsT0;
        JsonSerializer.Serialize(saved.Transactions.Single()).ShouldBe(JsonSerializer.Serialize(income));
    }
}
