using System.Diagnostics.CodeAnalysis;
using Shouldly;
using Xunit;
using YHAB.SharedKernel.Budgeting;
using static YHAB.UnitTests.Features.Budgeting.BudgetTestData;

namespace YHAB.UnitTests.Features.Budgeting;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class BudgetCalculatorTests
{
    [Fact]
    public void CreditOpeningDebtDoesNotCreateSpendableMoney()
    {
        var plan = Create(card: -300);
        var month = BudgetCalculator.Calculate(plan, January, plan.Today);
        month.ReadyToAssign.ShouldBe(1000);
        month.Categories.Single(item => item.Category.CreditAccountId.HasValue).Available.ShouldBe(0);
    }

    [Fact]
    public void PositiveBalancesCarryForwardAndFutureAssignmentsReserveMoney()
    {
        var plan = Assigned(Create(), 300);
        plan = plan with
        {
            Allocations = [.. plan.Allocations, new(plan.Categories[0].Id, January.AddMonths(1), 100)],
            Transactions = [Entry(plan, 0, -120)]
        };
        var january = BudgetCalculator.Calculate(plan, January, plan.Today);
        january.ReadyToAssign.ShouldBe(600);
        january.Categories[0].Available.ShouldBe(180);
        january.AssignedInFuture.ShouldBe(100);
        var february = BudgetCalculator.Calculate(plan, January.AddMonths(1), plan.Today);
        february.Categories[0].Available.ShouldBe(280);
        february.ReadyToAssign.ShouldBe(600);
    }

    [Fact]
    public void CashOverspendingReducesNextMonthReadyToAssign()
    {
        var plan = Assigned(Create(), 100);
        plan = plan with { Transactions = [Entry(plan, 0, -150)] };
        var january = BudgetCalculator.Calculate(plan, January, plan.Today);
        january.CashOverspending.ShouldBe(50);
        january.CreditOverspending.ShouldBe(0);
        january.ReadyToAssign.ShouldBe(900);
        var february = BudgetCalculator.Calculate(plan, January.AddMonths(1), plan.Today);
        february.Categories[0].Available.ShouldBe(0);
        february.ReadyToAssign.ShouldBe(850);
    }

    [Fact]
    public void FundedCreditPurchasesMoveCashToPaymentCategory()
    {
        var plan = Assigned(Create(), 100);
        plan = plan with { Transactions = [Entry(plan, 1, -80)] };
        var month = BudgetCalculator.Calculate(plan, January, plan.Today);
        month.Categories[0].Available.ShouldBe(20);
        month.Categories[1].Available.ShouldBe(80);
        month.ReadyToAssign.ShouldBe(900);
        month.CreditOverspending.ShouldBe(0);
    }

    [Fact]
    public void CreditOverspendingBecomesDebtWithoutReducingNextMonthCash()
    {
        var plan = Assigned(Create(), 100);
        plan = plan with { Transactions = [Entry(plan, 1, -150)] };
        var january = BudgetCalculator.Calculate(plan, January, plan.Today);
        january.CreditOverspending.ShouldBe(50);
        january.CashOverspending.ShouldBe(0);
        january.Categories[1].Available.ShouldBe(100);
        var february = BudgetCalculator.Calculate(plan, January.AddMonths(1), plan.Today);
        february.ReadyToAssign.ShouldBe(900);
        february.Categories[0].Available.ShouldBe(0);
        february.Categories[1].Available.ShouldBe(100);
    }

    [Fact]
    public void CashSpendingHasPriorityWhenACategoryUsesCashAndCredit()
    {
        var plan = Assigned(Create(), 100);
        plan = plan with { Transactions = [Entry(plan, 1, -150), Entry(plan, 0, -20)] };
        var month = BudgetCalculator.Calculate(plan, January, plan.Today);
        month.Categories[1].Available.ShouldBe(80);
        month.CreditOverspending.ShouldBe(70);
        month.ReadyToAssign.ShouldBe(900);
    }

    [Fact]
    public void CardPaymentReducesReserveWithoutCountingAsAnotherPurchase()
    {
        var plan = Assigned(Create(), 100);
        var payment = Entry(plan, 0, -80, date: January.AddDays(5)) with { TransferAccountId = plan.Accounts[1].Id, Splits = [] };
        plan = plan with { Transactions = [Entry(plan, 1, -80), payment] };
        var month = BudgetCalculator.Calculate(plan, January, plan.Today);
        month.Categories[0].Activity.ShouldBe(-80);
        month.Categories[1].Available.ShouldBe(0);
        month.ReadyToAssign.ShouldBe(900);
        BudgetFacts.Balance(plan, plan.Accounts[1], plan.Today).Working.ShouldBe(0);
        ReportCalculator.Months(plan, January, plan.Today).Single().Expense.ShouldBe(80);
    }

    [Fact]
    public void PositiveCreditBalanceSpendingUsesCashBeforeCreatingDebt()
    {
        var plan = Assigned(Create(card: 50), 100);
        plan = plan with { Transactions = [Entry(plan, 1, -80)] };
        var month = BudgetCalculator.Calculate(plan, January, plan.Today);
        month.Categories[1].Available.ShouldBe(30);
        month.Categories[0].Available.ShouldBe(20);
        month.ReadyToAssign.ShouldBe(950);
    }

    [Fact]
    public void RefundRestoresCategoryAndReducesPaymentReserve()
    {
        var plan = Assigned(Create(), 100);
        plan = plan with { Transactions = [Entry(plan, 1, -80), Entry(plan, 1, 30, date: January.AddDays(3))] };
        var month = BudgetCalculator.Calculate(plan, January, plan.Today);
        month.Categories[0].Available.ShouldBe(50);
        month.Categories[1].Available.ShouldBe(50);
        ReportCalculator.Spending(plan, January, plan.Today).Single().Amount.ShouldBe(50);
    }

    [Fact]
    public void SplitRoundingPreservesEveryCentOfPositiveCreditCrossing()
    {
        var plan = Assigned(Create(card: 0.01m), 1);
        var entry = Entry(plan, 1, -0.03m) with { Splits = Enumerable.Range(0, 3).Select(_ => new SplitData(Guid.NewGuid(), plan.Categories[0].Id, -0.01m, "")).ToArray() };
        plan = plan with { Transactions = [entry] };
        var month = BudgetCalculator.Calculate(plan, January, plan.Today);
        month.Categories[1].Available.ShouldBe(0.02m);
        month.ReadyToAssign.ShouldBe(999.01m);
    }

    [Fact]
    public void TemplatesDoNotAffectBalancesOrReportsUntilPosted()
    {
        var plan = Create();
        plan = plan with { Transactions = [Entry(plan, 0, -100) with { Repeat = RepeatFrequency.Monthly }] };
        BudgetFacts.Balance(plan, plan.Accounts[0], plan.Today).Working.ShouldBe(1000);
        BudgetCalculator.Calculate(plan, January, plan.Today).ReadyToAssign.ShouldBe(1000);
        ReportCalculator.Spending(plan, January, plan.Today).ShouldBeEmpty();
    }

    [Fact]
    public void CreditBalanceTransferMovesFundedPaymentMoneyToTheNewCard()
    {
        var plan = Assigned(Create(), 100);
        var other = plan.Accounts[1] with { Id = Guid.NewGuid(), Name = "Other card" };
        var payment = plan.Categories[1] with { Id = Guid.NewGuid(), Name = "Other card", CreditAccountId = other.Id };
        var transfer = Entry(plan, 1, 100, date: January.AddDays(5)) with { TransferAccountId = other.Id, Splits = [] };
        plan = plan with
        {
            Accounts = [.. plan.Accounts, other],
            Categories = [.. plan.Categories, payment],
            Transactions = [Entry(plan, 1, -100), transfer]
        };
        var month = BudgetCalculator.Calculate(plan, January, plan.Today);
        month.Categories[1].Available.ShouldBe(0);
        month.Categories[2].Available.ShouldBe(100);
        month.ReadyToAssign.ShouldBe(900);
    }
}
