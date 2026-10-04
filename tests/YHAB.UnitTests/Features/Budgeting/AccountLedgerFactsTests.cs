using System.Diagnostics.CodeAnalysis;
using Shouldly;
using Xunit;
using YHAB.Features.Budgeting.Services;
using YHAB.SharedKernel.Budgeting;
using static YHAB.UnitTests.Features.Budgeting.BudgetTestData;

namespace YHAB.UnitTests.Features.Budgeting;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class AccountLedgerFactsTests
{
    [Fact]
    public void FactsIncludeBothTransferSidesButExcludeTemplatesFutureAmountsAndOpeningBalances()
    {
        var plan = Create();
        plan = plan with
        {
            Transactions = [Entry(plan, 0, -10), Entry(plan, 0, -20) with { State = ClearingState.Cleared },
            Entry(plan, 0, -30) with { State = ClearingState.Reconciled },
            Entry(plan, 1, -40) with { TransferAccountId = plan.Accounts[0].Id, TransferState = ClearingState.Cleared },
            Entry(plan, 0, -50) with { TransferAccountId = plan.Accounts[1].Id, State = ClearingState.Cleared, TransferState = ClearingState.Reconciled },
            Entry(plan, 1, -999), Entry(plan, 0, -888, date: January) with { Repeat = RepeatFrequency.Monthly },
            Entry(plan, 0, 777, date: January.AddMonths(1)) with { State = ClearingState.Cleared }]
        };
        AccountLedgerFacts.FromLedger(plan, plan.Accounts[0].Id, plan.Today).ShouldBe(new(January, true, -70, -60));
        AccountLedgerFacts.FromLedger(plan, Guid.NewGuid(), plan.Today).ShouldBe(new(null, false, 0, 0));
        var before = AccountLedgerFacts.FromLedger(plan, plan.Accounts[0].Id, January);
        before.ShouldBe(new(January, true, 0, 0));
        AccountLedgerFacts.FromLedger(plan, plan.Accounts[0].Id, January.AddDays(1)).ShouldBe(new(January, true, -70, -60));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OtherTransferSideCannotLockTheAccountsOpening(bool destination)
    {
        var plan = Create();
        var transfer = Entry(plan, destination ? 1 : 0, -25) with
        {
            TransferAccountId = plan.Accounts[destination ? 0 : 1].Id,
            State = destination ? ClearingState.Reconciled : ClearingState.Uncleared,
            TransferState = destination ? ClearingState.Uncleared : ClearingState.Reconciled,
        };
        plan = plan with { Transactions = [transfer] };
        var facts = AccountLedgerFacts.FromLedger(plan, plan.Accounts[0].Id, plan.Today);
        facts.ShouldBe(new(transfer.Date, false, destination ? 25 : -25, 0));
        CatalogChanges.Save(NewIds(), plan with { Transactions = [] }, new(0, plan.Accounts[0] with { OpeningBalance = 900 }), plan.Today, facts)
            .AsT0.Accounts.Single(item => item.Id == plan.Accounts[0].Id).OpeningBalance.ShouldBe(900);
    }

    [Fact]
    public void ReconciliationCanUseAggregatesWithoutLoadingPreviouslyReconciledHistory()
    {
        var plan = Create();
        var cleared = Entry(plan, 0, -20) with { State = ClearingState.Cleared };
        plan = plan with { Transactions = [cleared] };
        var result = MoneyChanges.Reconcile(NewIds(), plan, new(0, plan.Accounts[0].Id, plan.Today, 945, true), plan.Today, clearedMovement: -50).AsT0;
        result.Transactions.Single(item => item.Id == cleared.Id).State.ShouldBe(ClearingState.Reconciled);
        var adjustment = result.Transactions.Single(item => item.Id != cleared.Id);
        adjustment.Amount.ShouldBe(-5);
        adjustment.State.ShouldBe(ClearingState.Reconciled);
        adjustment.Splits.ShouldBeEmpty();
        plan.Transactions.Single().State.ShouldBe(ClearingState.Cleared);
    }
}
