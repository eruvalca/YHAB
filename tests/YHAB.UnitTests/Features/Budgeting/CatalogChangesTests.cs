using System.Diagnostics.CodeAnalysis;
using Shouldly;
using Xunit;
using YHAB.Features.Budgeting.Services;
using YHAB.SharedKernel.Budgeting;
using static YHAB.UnitTests.Features.Budgeting.BudgetTestData;

namespace YHAB.UnitTests.Features.Budgeting;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class CatalogChangesTests
{
    [Fact]
    public void CreditAccountCreatesOnePaymentCategoryAndRenamingKeepsItsIdentity()
    {
        var plan = Create();
        var account = new AccountData(Guid.Empty, "Travel card", AccountKind.CreditCard, -200, January, false, "");
        var created = CatalogChanges.Save(BudgetTestData.NewIds(), plan, new(0, account), plan.Today).AsT0;
        var card = created.Accounts.Single(item => string.Equals(item.Name, "Travel card", StringComparison.Ordinal));
        var payment = created.Categories.Single(item => item.CreditAccountId == card.Id);
        var renamed = CatalogChanges.Save(BudgetTestData.NewIds(), created, new(0, card with { Name = "Rewards card" }), plan.Today).AsT0;
        renamed.Categories.Single(item => item.CreditAccountId == card.Id).Id.ShouldBe(payment.Id);
        renamed.Categories.Single(item => item.CreditAccountId == card.Id).Name.ShouldBe("Rewards card");
        renamed.Groups.Single(item => item.Id == payment.GroupId).Name.ShouldBe("Credit card payments");
        BudgetCalculator.Calculate(renamed, January, plan.Today).ReadyToAssign.ShouldBe(1000);
    }

    [Fact]
    public void MergingAUsedCategoryPreservesAssignmentsSpendingAndAccountBalance()
    {
        var plan = Create();
        var replacement = plan.Categories[0] with { Id = Guid.NewGuid(), Name = "Food" };
        plan = plan with
        {
            Categories = [.. plan.Categories, replacement],
            Allocations = [new(plan.Categories[0].Id, January, 300), new(replacement.Id, January, 50)],
            Transactions = [Entry(plan, 0, -75)],
        };
        CatalogChanges.Remove(plan, new(0, plan.Categories[0].Id, null)).IsT1.ShouldBeTrue();
        var merged = CatalogChanges.Remove(plan, new(0, plan.Categories[0].Id, replacement.Id)).AsT0;
        merged.Categories.ShouldNotContain(item => item.Id == plan.Categories[0].Id);
        merged.Allocations.Single().Amount.ShouldBe(350);
        merged.Transactions.Single().Splits.Single().CategoryId.ShouldBe(replacement.Id);
        var month = BudgetCalculator.Calculate(merged, January, plan.Today);
        month.ReadyToAssign.ShouldBe(650);
        month.Categories.Single(item => item.Category.Id == replacement.Id).Available.ShouldBe(275);
        BudgetFacts.Balance(merged, merged.Accounts[0], plan.Today).Working.ShouldBe(925);
    }

    [Fact]
    public void AutoAssignUsesOnlyReadyCashAndSkipsHiddenOrSnoozedTargets()
    {
        var plan = Create(cash: 100);
        var category = plan.Categories[0] with { Target = new(TargetKind.SetAside, TargetCadence.Monthly, 150, January, null) };
        var hidden = category with { Id = Guid.NewGuid(), Hidden = true, SortOrder = -2 };
        var snoozed = category with { Id = Guid.NewGuid(), SortOrder = -1 };
        plan = plan with { Categories = [category, hidden, snoozed, plan.Categories[1]], Allocations = [new(snoozed.Id, January, 0, true)] };
        var funded = MoneyChanges.AutoAssign(plan, new(0, January), plan.Today).AsT0;
        funded.Allocations.Single(item => item.CategoryId == category.Id).Amount.ShouldBe(100);
        funded.Allocations.ShouldNotContain(item => item.CategoryId == hidden.Id);
        funded.Allocations.Single(item => item.CategoryId == snoozed.Id).Amount.ShouldBe(0);
        var month = BudgetCalculator.Calculate(funded, January, plan.Today);
        month.ReadyToAssign.ShouldBe(0);
        month.Categories.Single(item => item.Category.Id == category.Id).TargetNeeded.ShouldBe(50);
    }

    [Fact]
    public void PayeeRenameChangesMatchingPostedAndRecurringEntriesOnly()
    {
        var plan = Create();
        plan = plan with { Transactions = [Entry(plan, 0, -10), Entry(plan, 0, -20) with { Repeat = RepeatFrequency.Monthly }, Entry(plan, 0, -30) with { Payee = "Other" }] };
        var renamed = PlanCommandHandler.Apply(BudgetTestData.NewIds(), plan, new RenamePayee(0, "market", "Neighborhood market"), plan.Today).AsT0;
        renamed.Transactions.Take(2).ShouldAllBe(item => string.Equals(item.Payee, "Neighborhood market", StringComparison.Ordinal));
        renamed.Transactions[2].Payee.ShouldBe("Other");
        renamed.Transactions.Select(item => item.Amount).ShouldBe([-10, -20, -30]);
        renamed.Transactions[1].Repeat.ShouldBe(RepeatFrequency.Monthly);
    }
}
