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
            Transactions = [Entry(plan, 0, -10), Entry(plan, 0, -20), Entry(plan, 0, -30),
            Entry(plan, 1, -40) with { TransferAccountId = plan.Accounts[0].Id },
            Entry(plan, 0, -50) with { TransferAccountId = plan.Accounts[1].Id },
            Entry(plan, 1, -999), Entry(plan, 0, -888, date: January) with { Repeat = RepeatFrequency.Monthly },
            Entry(plan, 0, 777, date: January.AddMonths(1))]
        };
        AccountLedgerFacts.FromLedger(plan, plan.Accounts[0].Id, plan.Today).ShouldBe(new(January, -70));
        AccountLedgerFacts.FromLedger(plan, Guid.NewGuid(), plan.Today).ShouldBe(new(null, 0));
        var before = AccountLedgerFacts.FromLedger(plan, plan.Accounts[0].Id, January);
        before.ShouldBe(new(January, 0));
        AccountLedgerFacts.FromLedger(plan, plan.Accounts[0].Id, January.AddDays(1)).ShouldBe(new(January, -70));
    }

    [Fact]
    public void FirstTransactionUsesEveryEntryWhileMovementUsesPostedEntriesOnly()
    {
        var plan = Create();
        var template = Entry(plan, 0, -25, date: January) with { Repeat = RepeatFrequency.Daily };
        var future = Entry(plan, 0, -40, date: plan.Today.AddDays(1));
        plan = plan with { Transactions = [template, future] };
        var facts = AccountLedgerFacts.FromLedger(plan, plan.Accounts[0].Id, plan.Today);
        facts.FirstTransaction.ShouldBe(January);
        facts.WorkingMovement.ShouldBe(0);
    }
}
