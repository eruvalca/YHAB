using System.Diagnostics.CodeAnalysis;
using Shouldly;
using Xunit;
using YHAB.Features.Budgeting.Services;
using YHAB.SharedKernel.Budgeting;
using static YHAB.UnitTests.Features.Budgeting.BudgetTestData;

namespace YHAB.UnitTests.Features.Budgeting;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class LedgerBoundaryTests
{
    [Fact]
    public void TrackingTransfersDoNotInventIncomeExpensesOrBudgetCash()
    {
        var plan = Create();
        var asset = plan.Accounts[0] with { Id = Guid.NewGuid(), Kind = AccountKind.Asset, OpeningBalance = 500 };
        var other = asset with { Id = Guid.NewGuid(), OpeningBalance = 0 };
        plan = plan with { Accounts = [.. plan.Accounts, asset, other] };
        plan = plan with
        {
            Transactions =
            [
                Entry(plan, 2, -100, category: -1) with { TransferAccountId = plan.Accounts[0].Id },
                Entry(plan, 2, 50) with { Splits = [] },
                Entry(plan, 2, -20) with { TransferAccountId = other.Id, Splits = [] },
                Entry(plan, 0, -30) with { TransferAccountId = asset.Id },
            ],
        };
        var report = ReportCalculator.Months(plan, January, plan.Today).Single();
        report.Income.ShouldBe(100);
        report.Expense.ShouldBe(30);
        report.Assets.ShouldBe(1550);
        ReportCalculator.Spending(plan, January, plan.Today).Single().Amount.ShouldBe(30);
        var month = BudgetCalculator.Calculate(plan, January, plan.Today);
        month.ReadyToAssign.ShouldBe(1100);
        month.CashOverspending.ShouldBe(30);
        BudgetCalculator.Calculate(plan, January.AddMonths(1), plan.Today).ReadyToAssign.ShouldBe(1070);
    }

    [Fact]
    public void TargetStartingBeforeThePlanHasZeroHistoricalCarry()
    {
        var plan = Create();
        plan = plan with { Categories = [plan.Categories[0] with { Target = new(TargetKind.Refill, TargetCadence.Custom, 300, January.AddMonths(-1), January.AddMonths(2)) }, plan.Categories[1]] };
        BudgetCalculator.Calculate(plan, January.AddMonths(1), plan.Today).Categories[0].TargetNeeded.ShouldBe(150);
    }

    [Fact]
    public void ZeroDollarCreditEntryLeavesMoneyAndReservesUnchanged()
    {
        var plan = Create(card: -100);
        plan = plan with { Transactions = [Entry(plan, 1, 0)] };
        var month = BudgetCalculator.Calculate(plan, January, plan.Today);
        month.ReadyToAssign.ShouldBe(1000);
        month.Available.ShouldBe(0);
        month.CashOverspending.ShouldBe(0);
        month.CreditOverspending.ShouldBe(0);
        month.Categories.ShouldAllBe(item => item.Activity == 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreditBalanceTransfersHaveTheSameResultFromEitherAccountPerspective(bool reverse)
    {
        var plan = Create(card: -100);
        var card = plan.Accounts[1] with { Id = Guid.NewGuid(), OpeningBalance = 0 };
        var payment = plan.Categories[1] with { Id = Guid.NewGuid(), CreditAccountId = card.Id };
        plan = plan with { Accounts = [.. plan.Accounts, card], Categories = [.. plan.Categories, payment], Allocations = [new(plan.Categories[1].Id, January, 100)] };
        var transfer = reverse
            ? Entry(plan, 2, -100) with { TransferAccountId = plan.Accounts[1].Id, Splits = [] }
            : Entry(plan, 1, 100) with { TransferAccountId = card.Id, Splits = [] };
        var month = BudgetCalculator.Calculate(plan with { Transactions = [transfer] }, January, plan.Today);
        month.Categories[1].Available.ShouldBe(0);
        month.Categories[2].Available.ShouldBe(100);
        month.ReadyToAssign.ShouldBe(900);
    }

    [Fact]
    public void CreditPaymentsFromTrackingDoNotConsumeBudgetCashReserves()
    {
        var plan = Create(card: -100);
        var asset = plan.Accounts[0] with { Id = Guid.NewGuid(), Kind = AccountKind.Asset, OpeningBalance = 100 };
        plan = plan with { Accounts = [.. plan.Accounts, asset] };
        plan = plan with { Transactions = [Entry(plan, 2, -50, category: -1) with { TransferAccountId = plan.Accounts[1].Id }] };
        var month = BudgetCalculator.Calculate(plan, January, plan.Today);
        month.ReadyToAssign.ShouldBe(1000);
        month.Categories[1].Available.ShouldBe(0);
        BudgetFacts.Balance(plan, plan.Accounts[1], plan.Today).Working.ShouldBe(-50);
        BudgetFacts.Balance(plan, asset, plan.Today).Working.ShouldBe(50);
    }

    [Fact]
    public void CatalogChangesProtectOpeningDatesAndUsedCategories()
    {
        var plan = Create();
        plan = plan with { Transactions = [Entry(plan, 0, -10, date: January)] };
        CatalogChanges.Save(BudgetTestData.NewIds(), plan, new SaveAccount(0, plan.Accounts[0] with { OpenedOn = January.AddDays(1) }), plan.Today).IsT1.ShouldBeTrue();
        CatalogChanges.Remove(plan, new(0, plan.Categories[0].Id, null)).IsT1.ShouldBeTrue();
        var replacement = plan.Categories[0] with { Id = Guid.NewGuid(), Name = "Replacement" };
        plan = plan with { Categories = [.. plan.Categories, replacement] };
        var removed = CatalogChanges.Remove(plan, new(0, plan.Categories[0].Id, replacement.Id)).AsT0;
        removed.Transactions[0].Splits[0].CategoryId.ShouldBe(replacement.Id);
        plan = plan with { Allocations = [new(plan.Categories[0].Id, January, AmountExpression.MaximumAmount), new(replacement.Id, January, 1)] };
        var overflow = CatalogChanges.Remove(plan, new(0, plan.Categories[0].Id, replacement.Id));
        overflow.IsT1.ShouldBeTrue();
        overflow.AsT1.Message.ShouldContain("combined assignments");
        var transfer = Entry(plan, 1, -20) with { TransferAccountId = plan.Accounts[0].Id, TransferState = ClearingState.Reconciled, Splits = [] };
        plan = plan with { Transactions = [transfer] };
        CatalogChanges.Save(BudgetTestData.NewIds(), plan, new SaveAccount(0, plan.Accounts[0] with { OpenedOn = January.AddDays(-1) }), plan.Today).IsT1.ShouldBeTrue();
    }

    [Fact]
    public void BulkDeletionAndStateChangesValidateSelectionAndPreserveOtherEntries()
    {
        var plan = Create();
        var entry = Entry(plan, 0, -10) with { NeedsApproval = true };
        var other = Entry(plan, 1, -20);
        plan = plan with { Transactions = [entry, other] };
        foreach (var ids in new IReadOnlyList<Guid>[] { null!, [], [Guid.NewGuid()] })
        {
            TransactionChanges.Delete(plan, new(0, ids)).IsT1.ShouldBeTrue();
            TransactionChanges.UpdateStates(plan, new(0, ids, ClearingState.Cleared, false)).IsT1.ShouldBeTrue();
        }
        TransactionChanges.UpdateStates(plan, new(0, [entry.Id], (ClearingState)99, false)).IsT1.ShouldBeTrue();
        TransactionChanges.UpdateStates(plan, new(0, [entry.Id], ClearingState.Reconciled, false)).IsT1.ShouldBeTrue();
        TransactionChanges.UpdateStates(plan, new(0, [entry.Id], ClearingState.Cleared, false, plan.Accounts[1].Id)).IsT1.ShouldBeTrue();
        var approved = TransactionChanges.UpdateStates(plan, new(0, [entry.Id], null, true)).AsT0;
        approved.Transactions[0].NeedsApproval.ShouldBeFalse();
        approved.Transactions[0].State.ShouldBe(ClearingState.Uncleared);
        approved.Transactions[1].ShouldBe(other);
        TransactionChanges.Delete(plan, new(0, [entry.Id])).AsT0.Transactions.ShouldBe([other]);
        var transfer = entry with { TransferAccountId = plan.Accounts[1].Id, TransferState = ClearingState.Cleared, Splits = [] };
        plan = plan with { Transactions = [transfer, other] };
        var destination = TransactionChanges.UpdateStates(plan, new(0, [transfer.Id], null, true, plan.Accounts[1].Id)).AsT0.Transactions[0];
        destination.TransferState.ShouldBe(ClearingState.Cleared);
        destination.State.ShouldBe(ClearingState.Uncleared);
        destination.NeedsApproval.ShouldBeFalse();
    }

    [Fact]
    public void BoundedCatchupEventuallyPostsEveryOccurrenceExactlyOnce()
    {
        var plan = Create();
        var templates = Enumerable.Range(0, 3).Select(_ => Entry(plan, 0, -1, date: January) with { Repeat = RepeatFrequency.Daily }).ToArray();
        plan = plan with { Transactions = templates, Today = January.AddDays(119) };
        var first = TransactionChanges.PostDue(BudgetTestData.NewIds(), plan, plan.Today);
        first.Transactions.Count.ShouldBe(131);
        var completed = TransactionChanges.PostDue(BudgetTestData.NewIds(), TransactionChanges.PostDue(BudgetTestData.NewIds(), first, plan.Today), plan.Today);
        completed.Transactions.Count.ShouldBe(363);
        completed.Transactions.Where(item => item.SourceTemplateId.HasValue).Select(item => (item.SourceTemplateId, item.ScheduledDate)).Distinct().Count().ShouldBe(360);
        TransactionChanges.PostDue(BudgetTestData.NewIds(), completed, plan.Today).Transactions.ShouldBe(completed.Transactions);
        plan.Transactions.ShouldBe(templates);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosingEitherTransferAccountPausesRecurrence(bool destination)
    {
        var plan = Create(cash: 0);
        var template = Entry(plan, 0, -10) with { Repeat = RepeatFrequency.Daily, TransferAccountId = plan.Accounts[1].Id, Splits = [] };
        var closedIndex = destination ? 1 : 0;
        plan = plan with
        {
            Accounts = plan.Accounts.Select((account, index) => index == closedIndex ? account with { Closed = true } : account).ToArray(),
            Transactions = [template],
        };
        TransactionChanges.PostDue(BudgetTestData.NewIds(), plan, plan.Today).Transactions.ShouldBe([template]);
    }
}
