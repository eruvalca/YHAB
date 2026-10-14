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
    public void FactsIncludeBothTransferSidesButExcludeTemplatesAndFutureAmounts()
    {
        var plan = Create();
        plan = plan with
        {
            Transactions = [Entry(plan, 0, -10), Entry(plan, 0, -20),
            Entry(plan, 0, -30),
            Entry(plan, 1, -40) with { TransferAccountId = plan.Accounts[0].Id },
            Entry(plan, 0, -50) with { TransferAccountId = plan.Accounts[1].Id },
            Entry(plan, 1, -999), Entry(plan, 0, -888, date: January) with { Repeat = RepeatFrequency.Monthly },
            Entry(plan, 0, 777, date: January.AddMonths(1))]
        };
        AccountLedgerFacts.FromLedger(plan, plan.Accounts[0].Id, plan.Today).ShouldBe(new(January, -70));
        AccountLedgerFacts.FromLedger(plan, Guid.NewGuid(), plan.Today).ShouldBe(new(null, 0));
        AccountLedgerFacts.FromLedger(plan, plan.Accounts[0].Id, January).ShouldBe(new(January, 0));
    }

    [Theory]
    [InlineData(false, -25)]
    [InlineData(true, 25)]
    public void AccountFactsUseTheSelectedSideOfATransfer(bool destination, decimal movement)
    {
        var plan = Create();
        var transfer = Entry(plan, destination ? 1 : 0, -25) with
        {
            TransferAccountId = plan.Accounts[destination ? 0 : 1].Id,
        };
        plan = plan with { Transactions = [transfer] };
        var facts = AccountLedgerFacts.FromLedger(plan, plan.Accounts[0].Id, plan.Today);

        facts.ShouldBe(new(transfer.Date, movement));
        CatalogChanges.Save(NewIds(), plan with { Transactions = [] },
                new(0, plan.Accounts[0] with { OpeningBalance = 900 }), plan.Today, facts)
            .AsT0.Accounts.Single(item => item.Id == plan.Accounts[0].Id).OpeningBalance.ShouldBe(900);
    }
}
