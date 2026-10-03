using System.Diagnostics.CodeAnalysis;
using Shouldly;
using Xunit;
using YHAB.Features.Budgeting.Models;
using YHAB.Features.Budgeting.Services;
using YHAB.SharedKernel.Budgeting;
using static YHAB.UnitTests.Features.Budgeting.BudgetTestData;

namespace YHAB.UnitTests.Features.Budgeting;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class TransactionChangesTests
{
    [Fact]
    public void RejectsOneOffFutureEntriesButAcceptsRepeatingInstructions()
    {
        var plan = Create();
        var entry = Entry(plan, 0, -25, date: plan.Today.AddDays(1));
        TransactionChanges.Save(plan, new(0, entry), plan.Today).IsT1.ShouldBeTrue();
        var result = TransactionChanges.Save(plan, new(0, entry with { Repeat = RepeatFrequency.Monthly }), plan.Today);
        result.IsT0.ShouldBeTrue();
        result.AsT0.Transactions.Single().AnchorDate.ShouldBe(entry.Date);
    }

    [Fact]
    public void DueRecurrencesArePostedOnceWithApprovalAndAnchorDayPreserved()
    {
        var plan = Create();
        var anchor = January.AddDays(30);
        var template = Entry(plan, 0, -25, date: anchor) with { Repeat = RepeatFrequency.Monthly, AnchorDate = anchor };
        plan = plan with { Transactions = [template] };
        var posted = TransactionChanges.PostDue(plan, new(2026, 3, 31));
        var occurrences = posted.Transactions.Where(item => item.SourceTemplateId == template.Id).OrderBy(item => item.Date).ToArray();
        occurrences.Select(item => item.Date).ShouldBe([new(2026, 1, 31), new(2026, 2, 28), new(2026, 3, 31)]);
        occurrences.ShouldAllBe(item => item.NeedsApproval && item.State == ClearingState.Uncleared && item.Amount == -25);
        posted.Transactions.Single(item => item.Id == template.Id).Date.ShouldBe(new(2026, 4, 30));
        TransactionChanges.PostDue(posted, new(2026, 3, 31)).Transactions.Count.ShouldBe(4);
        occurrences.SelectMany(item => item.Splits).Select(item => item.Id).Distinct().Count().ShouldBe(3);
    }

    [Fact]
    public void TransferClearingUpdatesOnlyTheSelectedAccountSide()
    {
        var plan = Create();
        var transfer = Entry(plan, 0, -100) with { TransferAccountId = plan.Accounts[1].Id, Splits = [] };
        plan = plan with { Transactions = [transfer] };
        var result = TransactionChanges.UpdateStates(plan, new(0, [transfer.Id], ClearingState.Cleared, false, plan.Accounts[1].Id));
        result.IsT0.ShouldBeTrue();
        result.AsT0.Transactions.Single().State.ShouldBe(ClearingState.Uncleared);
        result.AsT0.Transactions.Single().TransferState.ShouldBe(ClearingState.Cleared);
        BudgetFacts.Balance(result.AsT0, plan.Accounts[1], plan.Today).Cleared.ShouldBe(100);
        BudgetFacts.Balance(result.AsT0, plan.Accounts[0], plan.Today).Uncleared.ShouldBe(-100);
    }

    [Fact]
    public void InvalidSplitTotalsAndForeignCategoryAreRejected()
    {
        var plan = Create();
        var entry = Entry(plan, 0, -100);
        TransactionChanges.Save(plan, new(0, entry with { Amount = -99 }), plan.Today).IsT1.ShouldBeTrue();
        TransactionChanges.Save(plan, new(0, entry with { Splits = [entry.Splits[0] with { CategoryId = Guid.NewGuid() }] }), plan.Today).IsT1.ShouldBeTrue();
        TransactionChanges.Save(plan, new(0, entry with { Splits = [entry.Splits[0] with { Amount = decimal.MaxValue }, entry.Splits[0]] }), plan.Today).IsT1.ShouldBeTrue();
    }

    [Fact]
    public void ReconciliationLocksOnlyClearedEntriesAndRequiresExplicitAdjustment()
    {
        var plan = Create();
        var cleared = Entry(plan, 0, -100) with { State = ClearingState.Cleared };
        var pending = Entry(plan, 0, -50);
        plan = plan with { Transactions = [cleared, pending] };
        var rejected = MoneyChanges.Reconcile(plan, new(0, plan.Accounts[0].Id, plan.Today, 890, false), plan.Today);
        rejected.IsT1.ShouldBeTrue();
        rejected.AsT1.Message.ShouldContain("$10.00", Case.Insensitive);
        var result = MoneyChanges.Reconcile(plan, new(0, plan.Accounts[0].Id, plan.Today, 890, true), plan.Today);
        result.IsT0.ShouldBeTrue();
        var updated = result.AsT0;
        updated.Transactions.Single(item => item.Id == cleared.Id).State.ShouldBe(ClearingState.Reconciled);
        updated.Transactions.Single(item => item.Id == pending.Id).State.ShouldBe(ClearingState.Uncleared);
        updated.Transactions.Single(item => string.Equals(item.Payee, "Reconciliation adjustment", StringComparison.Ordinal)).Amount.ShouldBe(-10);
        BudgetFacts.Balance(updated, plan.Accounts[0], plan.Today).Cleared.ShouldBe(890);
        TransactionChanges.Delete(updated, new(0, [cleared.Id])).IsT1.ShouldBeTrue();
        TransactionChanges.Save(updated, new(0, cleared), plan.Today).IsT1.ShouldBeTrue();
    }

    [Fact]
    public void ReconciliationRejectsAnAdjustmentBeyondTheSupportedAmount()
    {
        var plan = Create();
        plan = plan with { Accounts = [plan.Accounts[0] with { OpeningBalance = -AmountExpression.MaximumAmount }, plan.Accounts[1]] };
        var result = MoneyChanges.Reconcile(plan, new(0, plan.Accounts[0].Id, plan.Today, AmountExpression.MaximumAmount, true), plan.Today);
        result.IsT1.ShouldBeTrue();
        result.AsT1.Message.ShouldContain("exceeds the supported transaction amount");
        plan.Transactions.ShouldBeEmpty();
    }

    [Fact]
    public void MovingMoneyChangesAssignmentsWithoutChangingCashOrTotalAssigned()
    {
        var plan = Assigned(Create(), 300);
        var result = MoneyChanges.Move(plan, new(0, plan.Categories[0].Id, plan.Categories[1].Id, January, 125), plan.Today);
        result.IsT0.ShouldBeTrue();
        var month = BudgetCalculator.Calculate(result.AsT0, January, plan.Today);
        month.Assigned.ShouldBe(300);
        month.Categories[0].Available.ShouldBe(175);
        month.Categories[1].Available.ShouldBe(125);
        month.ReadyToAssign.ShouldBe(700);
        MoneyChanges.Move(plan, new(0, plan.Categories[0].Id, null, January, 301), plan.Today).IsT1.ShouldBeTrue();
    }

    [Fact]
    public void ClosingAnAccountRequiresZeroBalanceAndOpeningChangesRespectReconciliation()
    {
        var plan = Create();
        CatalogChanges.Save(plan, new(0, plan.Accounts[0] with { Closed = true }), plan.Today).IsT1.ShouldBeTrue();
        plan = plan with { Transactions = [Entry(plan, 0, -50) with { State = ClearingState.Reconciled }] };
        CatalogChanges.Save(plan, new(0, plan.Accounts[0] with { OpeningBalance = 1100 }), plan.Today).IsT1.ShouldBeTrue();
        CatalogChanges.Save(plan, new(0, plan.Accounts[0] with { Name = "New name" }), plan.Today).IsT0.ShouldBeTrue();
    }
}
