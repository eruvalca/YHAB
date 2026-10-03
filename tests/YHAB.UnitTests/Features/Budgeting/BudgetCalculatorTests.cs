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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CrossCardRefundFundsPurchasesRegardlessOfTransactionOrder(bool refundFirst)
    {
        var plan = WithSecondCard(Create(card: -100));
        plan = plan with
        {
            Allocations = [new(plan.Categories[1].Id, January, 100)],
            Transactions =
            [
                Entry(plan, 2, -100, date: January.AddDays(refundFirst ? 3 : 2)),
                Entry(plan, 1, 100, date: January.AddDays(refundFirst ? 2 : 3)),
            ],
        };

        var month = BudgetCalculator.Calculate(plan, January, plan.Today);

        month.ReadyToAssign.ShouldBe(900);
        month.Categories[0].Available.ShouldBe(0);
        month.Categories[1].Available.ShouldBe(0);
        month.Categories[2].Available.ShouldBe(100);
        month.CashOverspending.ShouldBe(0);
        month.CreditOverspending.ShouldBe(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(40)]
    [InlineData(100)]
    public void BalanceTransferDoesNotUseMoneyFromLaterPurchases(decimal openingReserve)
    {
        var plan = WithSecondCard(Assigned(Create(card: -100), 100));
        var transfer = Entry(plan, 1, 100) with { TransferAccountId = plan.Accounts[2].Id, Splits = [] };
        plan = plan with
        {
            Allocations = [.. plan.Allocations, new(plan.Categories[1].Id, January, openingReserve)],
            Transactions = [transfer, Entry(plan, 1, -100, date: January.AddDays(3))],
        };

        var month = BudgetCalculator.Calculate(plan, January, plan.Today);

        month.Categories[1].Available.ShouldBe(100);
        month.Categories[2].Available.ShouldBe(openingReserve);
        month.ReadyToAssign.ShouldBe(900 - openingReserve);
    }

    [Fact]
    public void LaterCashSpendingTakesPriorityOverTransferredCreditPurchases()
    {
        var plan = WithSecondCard(Assigned(Create(), 100));
        var transfer = Entry(plan, 1, 100, date: January.AddDays(3)) with { TransferAccountId = plan.Accounts[2].Id, Splits = [] };
        plan = plan with { Transactions = [Entry(plan, 1, -100), transfer, Entry(plan, 0, -100, date: January.AddDays(4))] };

        var month = BudgetCalculator.Calculate(plan, January, plan.Today);

        month.Categories[1].Available.ShouldBe(0);
        month.Categories[2].Available.ShouldBe(0);
        month.CashOverspending.ShouldBe(0);
        month.CreditOverspending.ShouldBe(100);
        month.ReadyToAssign.ShouldBe(900);
    }

    [Fact]
    public void CashPaymentAfterBalanceTransferUsesTransferredReserve()
    {
        var plan = WithSecondCard(Assigned(Create(), 100));
        var transfer = Entry(plan, 1, 100, date: January.AddDays(3)) with { TransferAccountId = plan.Accounts[2].Id, Splits = [] };
        var payment = Entry(plan, 0, -100, date: January.AddDays(4)) with { TransferAccountId = plan.Accounts[2].Id, Splits = [] };
        plan = plan with { Transactions = [Entry(plan, 1, -100), transfer, payment] };

        var month = BudgetCalculator.Calculate(plan, January, plan.Today);

        month.Categories[1].Available.ShouldBe(0);
        month.Categories[2].Available.ShouldBe(0);
        month.ReadyToAssign.ShouldBe(900);
        month.CashOverspending.ShouldBe(0);
        BudgetFacts.Balance(plan, plan.Accounts[2], plan.Today).Working.ShouldBe(0);
    }

    [Theory]
    [InlineData(40, 60, 940)]
    [InlineData(100, 0, 1000)]
    [InlineData(150, 0, 1050)]
    public void PositiveCardBalanceUsedForTransferReservesOnlyBorrowedMoney(decimal positiveBalance, decimal reserve, decimal readyToAssign)
    {
        var plan = WithSecondCard(Create(card: -100));
        plan = plan with
        {
            Accounts = [plan.Accounts[0], plan.Accounts[1], plan.Accounts[2] with { OpeningBalance = positiveBalance }],
            Allocations = [new(plan.Categories[1].Id, January, 100)],
            Transactions = [Entry(plan, 1, 100) with { TransferAccountId = plan.Accounts[2].Id, Splits = [] }],
        };

        var month = BudgetCalculator.Calculate(plan, January, plan.Today);

        month.Categories[1].Available.ShouldBe(0);
        month.Categories[2].Available.ShouldBe(reserve);
        month.ReadyToAssign.ShouldBe(readyToAssign);
        month.CashOverspending.ShouldBe(0);
    }

    [Fact]
    public void CrossCardRefundCoversCashSpendingBeforeCreditSpending()
    {
        var plan = WithSecondCard(Create(card: -100));
        plan = plan with
        {
            Allocations = [new(plan.Categories[1].Id, January, 100)],
            Transactions =
            [
                Entry(plan, 2, -100),
                Entry(plan, 0, -100, date: January.AddDays(3)),
                Entry(plan, 1, 100, date: January.AddDays(4)),
            ],
        };

        var month = BudgetCalculator.Calculate(plan, January, plan.Today);

        month.Categories[0].Available.ShouldBe(-100);
        month.Categories[1].Available.ShouldBe(0);
        month.Categories[2].Available.ShouldBe(0);
        month.CashOverspending.ShouldBe(0);
        month.CreditOverspending.ShouldBe(100);
        month.ReadyToAssign.ShouldBe(900);
        var next = BudgetCalculator.Calculate(plan, January.AddMonths(1), plan.Today);
        next.Categories[0].Available.ShouldBe(0);
        next.ReadyToAssign.ShouldBe(900);
    }

    [Theory]
    [InlineData(false, 50)]
    [InlineData(false, 100)]
    [InlineData(true, 50)]
    [InlineData(true, 100)]
    public void UnfundedRefundDoesNotReserveMoneyOnAnotherCardOrReduceNextMonthCash(bool refundedCardFirst, decimal refund)
    {
        var plan = WithSecondCard(Create());
        plan = plan with
        {
            Transactions =
            [
                Entry(plan, 1, -100, date: January.AddDays(refundedCardFirst ? 3 : 2)),
                Entry(plan, 2, -100, date: January.AddDays(refundedCardFirst ? 2 : 3)),
                Entry(plan, 2, refund, date: January.AddDays(4)),
            ],
        };

        var next = BudgetCalculator.Calculate(plan, January.AddMonths(1), plan.Today);
        next.ReadyToAssign.ShouldBe(1000);
        next.Categories[1].Available.ShouldBe(0);
        next.Categories[2].Available.ShouldBe(0);
        var month = BudgetCalculator.Calculate(plan, January, plan.Today);
        month.ReadyToAssign.ShouldBe(1000);
        month.Categories[1].Available.ShouldBe(0);
        month.Categories[2].Available.ShouldBe(0);
        month.CashOverspending.ShouldBe(0);
        month.CreditOverspending.ShouldBe(200 - refund);
    }

    [Theory]
    [InlineData(50, 50, 0, 100)]
    [InlineData(150, 100, 50, 0)]
    public void PartialRefundReservesOnlyAssignedMoneyForRemainingCreditSpending(decimal assigned, decimal firstReserve, decimal secondReserve, decimal overspending)
    {
        var plan = WithSecondCard(Assigned(Create(), assigned));
        plan = plan with
        {
            Transactions =
            [
                Entry(plan, 1, -100),
                Entry(plan, 2, -100, date: January.AddDays(3)),
                Entry(plan, 2, 50, date: January.AddDays(4)),
            ],
        };

        var month = BudgetCalculator.Calculate(plan, January, plan.Today);
        month.Categories[1].Available.ShouldBe(firstReserve);
        month.Categories[2].Available.ShouldBe(secondReserve);
        month.CashOverspending.ShouldBe(0);
        month.CreditOverspending.ShouldBe(overspending);
        month.ReadyToAssign.ShouldBe(1000 - assigned);
        BudgetCalculator.Calculate(plan, January.AddMonths(1), plan.Today).ReadyToAssign.ShouldBe(1000 - assigned);
    }

    [Fact]
    public void UnfundedRefundDoesNotCoverCashOverspending()
    {
        var plan = WithSecondCard(Create());
        plan = plan with
        {
            Transactions =
            [
                Entry(plan, 1, -100),
                Entry(plan, 2, -100, date: January.AddDays(3)),
                Entry(plan, 2, 100, date: January.AddDays(4)),
                Entry(plan, 0, -100, date: January.AddDays(5)),
            ],
        };

        var month = BudgetCalculator.Calculate(plan, January, plan.Today);
        month.Categories[1].Available.ShouldBe(0);
        month.Categories[2].Available.ShouldBe(0);
        month.CashOverspending.ShouldBe(100);
        month.CreditOverspending.ShouldBe(100);
        month.ReadyToAssign.ShouldBe(1000);
        BudgetCalculator.Calculate(plan, January.AddMonths(1), plan.Today).ReadyToAssign.ShouldBe(900);
    }

    [Theory]
    [InlineData(0, 100, 0, 0)]
    [InlineData(40, 100, 0, 40)]
    [InlineData(100, 100, 0, 100)]
    [InlineData(150, 50, 50, 100)]
    public void RefundOffsetsCannotFundAnEarlierBalanceTransfer(decimal assigned, decimal refund, decimal firstReserve, decimal secondReserve)
    {
        var plan = WithSecondCard(Assigned(Create(), assigned));
        var transfer = Entry(plan, 1, 100, date: January.AddDays(3)) with { TransferAccountId = plan.Accounts[2].Id, Splits = [] };
        plan = plan with
        {
            Transactions =
            [
                Entry(plan, 1, -100),
                transfer,
                Entry(plan, 1, -100, date: January.AddDays(4)),
                Entry(plan, 1, refund, date: January.AddDays(5)),
            ],
        };

        var month = BudgetCalculator.Calculate(plan, January, plan.Today);
        month.Categories[1].Available.ShouldBe(firstReserve);
        month.Categories[2].Available.ShouldBe(secondReserve);
        month.CashOverspending.ShouldBe(0);
        month.CreditOverspending.ShouldBe(Math.Max(0, 200 - refund - assigned));
        month.ReadyToAssign.ShouldBe(1000 - assigned);
        var next = BudgetCalculator.Calculate(plan, January.AddMonths(1), plan.Today);
        next.Categories[1].Available.ShouldBe(firstReserve);
        next.Categories[2].Available.ShouldBe(secondReserve);
        next.ReadyToAssign.ShouldBe(1000 - assigned);
    }

    [Fact]
    public void RefundBeforePurchaseReleasesOpeningReserveBeforeBalanceTransfer()
    {
        var plan = WithSecondCard(Create(card: -200));
        var transfer = Entry(plan, 1, 100, date: January.AddDays(3)) with { TransferAccountId = plan.Accounts[2].Id, Splits = [] };
        plan = plan with
        {
            Allocations = [new(plan.Categories[1].Id, January, 100)],
            Transactions =
            [
                Entry(plan, 1, 100),
                transfer,
                Entry(plan, 1, -100, date: January.AddDays(4)),
            ],
        };

        var month = BudgetCalculator.Calculate(plan, January, plan.Today);
        month.Categories[1].Available.ShouldBe(100);
        month.Categories[2].Available.ShouldBe(0);
        month.CashOverspending.ShouldBe(0);
        month.ReadyToAssign.ShouldBe(900);
        BudgetCalculator.Calculate(plan, January.AddMonths(1), plan.Today).ReadyToAssign.ShouldBe(900);
    }

    [Theory]
    [InlineData(0, 0, 0, 120)]
    [InlineData(50, 0, 50, 70)]
    [InlineData(150, 20, 100, 0)]
    public void PartialRefundsAcrossMultiplePurchasesPreserveOnlyFundedReserves(decimal assigned, decimal firstReserve, decimal secondReserve, decimal overspending)
    {
        var plan = WithSecondCard(Assigned(Create(), assigned));
        plan = plan with
        {
            Transactions =
            [
                Entry(plan, 2, -100),
                Entry(plan, 1, -40, date: January.AddDays(3)),
                Entry(plan, 1, -60, date: January.AddDays(4)),
                Entry(plan, 1, 30, date: January.AddDays(5)),
                Entry(plan, 1, 50, date: January.AddDays(6)),
            ],
        };

        var month = BudgetCalculator.Calculate(plan, January, plan.Today);
        month.Categories[0].Available.ShouldBe(assigned - 120);
        month.Categories[1].Available.ShouldBe(firstReserve);
        month.Categories[2].Available.ShouldBe(secondReserve);
        month.CashOverspending.ShouldBe(0);
        month.CreditOverspending.ShouldBe(overspending);
        month.ReadyToAssign.ShouldBe(1000 - assigned);
        BudgetCalculator.Calculate(plan, January.AddMonths(1), plan.Today).ReadyToAssign.ShouldBe(1000 - assigned);
    }

    private static PlanSnapshot WithSecondCard(PlanSnapshot plan)
    {
        var card = plan.Accounts[1] with { Id = Guid.NewGuid(), Name = "Other card", OpeningBalance = 0 };
        var payment = plan.Categories[1] with { Id = Guid.NewGuid(), Name = card.Name, CreditAccountId = card.Id };
        return plan with { Accounts = [.. plan.Accounts, card], Categories = [.. plan.Categories, payment] };
    }
}
