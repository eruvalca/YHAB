using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Shouldly;
using Xunit;
using YHAB.Features.Budgeting.Services;
using YHAB.SharedKernel.Budgeting;
using static YHAB.UnitTests.Features.Budgeting.BudgetTestData;

namespace YHAB.UnitTests.Features.Budgeting;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class MoneyPolicyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProjectedFundingUsesHistoricalBalancesWithOnlyCurrentAssignments(bool move)
    {
        var plan = Create(cash: 1000);
        var category = plan.Categories[0] with { Target = new(TargetKind.Balance, TargetCadence.Monthly, 500, January, null) };
        var month = January.AddMonths(1);
        plan = plan with
        {
            Categories = [category, plan.Categories[1]],
            Allocations = [new(category.Id, January, 100), new(category.Id, month, 20), new(category.Id, month.AddMonths(1), 50)],
            Transactions = [Entry(plan, 0, -60)],
        };
        var projection = BudgetCalculator.Calculate(plan, month, plan.Today);
        var current = plan with { Allocations = [plan.Allocations[1]], Transactions = [] };
        PlanCommand command = move ? new MoveMoney(0, category.Id, null, month, 30) : new AutoAssign(0, month);
        var result = PlanCommandHandler.Apply(NewIds(), current, command, plan.Today, projection).AsT0;
        result.Allocations.Single().Amount.ShouldBe(move ? -10 : 460);
        result.Transactions.ShouldBeEmpty();
        current.Allocations.Single().Amount.ShouldBe(20);
        var full = PlanCommandHandler.Apply(NewIds(), plan, command, plan.Today).AsT0;
        result.Allocations.ShouldBe(full.Allocations.Where(item => item.Month == month));
    }

    [Theory]
    [InlineData("assign-category")]
    [InlineData("assign-month")]
    [InlineData("assign-cents")]
    [InlineData("move-same")]
    [InlineData("move-month")]
    [InlineData("move-negative")]
    [InlineData("move-cents")]
    [InlineData("move-source")]
    [InlineData("move-destination")]
    [InlineData("move-insufficient")]
    [InlineData("auto-month")]
    [InlineData("reconcile-account")]
    [InlineData("reconcile-date")]
    [InlineData("reconcile-before-opening")]
    [InlineData("reconcile-cents")]
    public void InvalidMoneyCommandsLeaveAllBalancesAndInputsUnchanged(string scenario)
    {
        var plan = Assigned(Create(), 100);
        var category = plan.Categories[0].Id;
        PlanCommand command = scenario switch
        {
            "assign-category" => new AssignMoney(0, Guid.NewGuid(), January, 10),
            "assign-month" => new AssignMoney(0, category, January.AddDays(1), 10),
            "assign-cents" => new AssignMoney(0, category, January, 0.001m),
            "move-same" => new MoveMoney(0, category, category, January, 10),
            "move-month" => new MoveMoney(0, null, category, new(1999, 1, 1), 10),
            "move-negative" => new MoveMoney(0, null, category, January, -1),
            "move-cents" => new MoveMoney(0, null, category, January, 0.001m),
            "move-source" => new MoveMoney(0, Guid.NewGuid(), category, January, 10),
            "move-destination" => new MoveMoney(0, category, Guid.NewGuid(), January, 10),
            "move-insufficient" => new MoveMoney(0, null, category, January, 901),
            "auto-month" => new AutoAssign(0, January.AddDays(1)),
            "reconcile-account" => new ReconcileAccount(0, Guid.NewGuid(), January, 1000, false),
            "reconcile-date" => new ReconcileAccount(0, plan.Accounts[0].Id, plan.Today.AddDays(1), 1000, false),
            "reconcile-before-opening" => new ReconcileAccount(0, plan.Accounts[0].Id, January.AddDays(-1), 1000, false),
            _ => new ReconcileAccount(0, plan.Accounts[0].Id, January, 1000.001m, true),
        };
        var original = JsonSerializer.Serialize(plan);
        var outcome = PlanCommandHandler.Apply(BudgetTestData.NewIds(), plan, command, plan.Today);
        outcome.IsT1.ShouldBeTrue();
        outcome.AsT1.Message.ShouldNotBeNullOrWhiteSpace();
        JsonSerializer.Serialize(plan).ShouldBe(original);
    }

    [Fact]
    public void MovesBetweenReadyToAssignAndCategoriesPreserveSnoozesAndOtherMonths()
    {
        var plan = Create();
        var category = plan.Categories[0].Id;
        plan = plan with { Allocations = [new(category, January, 100, true), new(category, January.AddMonths(1), 40)] };
        var funded = MoneyChanges.Move(plan, new(0, null, category, January, 50), plan.Today).AsT0;
        funded.Allocations.Single(item => item.Month == January).ShouldBe(new(category, January, 150, true));
        funded.Allocations.Single(item => item.Month == January.AddMonths(1)).Amount.ShouldBe(40);
        BudgetCalculator.Calculate(funded, January, plan.Today).ReadyToAssign.ShouldBe(810);
        var released = MoneyChanges.Move(funded, new(0, category, null, January, 150), plan.Today).AsT0;
        released.Allocations.Single(item => item.Month == January).Amount.ShouldBe(0);
        BudgetCalculator.Calculate(released, January, plan.Today).ReadyToAssign.ShouldBe(960);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MovesCannotOverflowEitherAssignment(bool source)
    {
        var plan = Create(cash: AmountExpression.MaximumAmount);
        var first = plan.Categories[0].Id;
        var second = plan.Categories[1].Id;
        plan = plan with
        {
            Allocations = source
                ? [new(first, January, -AmountExpression.MaximumAmount)]
                : [new(first, January, 10), new(second, January, AmountExpression.MaximumAmount)],
            Transactions = source ? [Entry(plan, 0, AmountExpression.MaximumAmount), Entry(plan, 0, 10)] : [],
        };
        var outcome = MoneyChanges.Move(plan, new(0, first, second, January, 1), plan.Today);
        outcome.IsT1.ShouldBeTrue();
        outcome.AsT1.Message.ShouldContain("exceed");
    }

    [Fact]
    public void ReconciliationLocksOnlyEligibleTransferSidesAndLeavesFutureAndRecurringEntries()
    {
        var plan = Create();
        var transfer = Entry(plan, 1, -20) with { TransferAccountId = plan.Accounts[0].Id, TransferState = ClearingState.Cleared, Splits = [] };
        var future = Entry(plan, 0, -10, date: plan.Today) with { State = ClearingState.Cleared };
        var recurring = Entry(plan, 0, -10) with { Repeat = RepeatFrequency.Monthly, State = ClearingState.Cleared };
        var unrelated = Entry(plan, 1, -5) with { State = ClearingState.Cleared };
        plan = plan with { Transactions = [transfer, future, recurring, unrelated] };
        var result = MoneyChanges.Reconcile(BudgetTestData.NewIds(), plan, new(0, plan.Accounts[0].Id, January.AddDays(2), 1020, false), plan.Today).AsT0;
        result.Transactions.Count.ShouldBe(4);
        result.Transactions[0].TransferState.ShouldBe(ClearingState.Reconciled);
        result.Transactions[0].State.ShouldBe(ClearingState.Uncleared);
        result.Transactions.Skip(1).ShouldBe(plan.Transactions.Skip(1));
    }
}
