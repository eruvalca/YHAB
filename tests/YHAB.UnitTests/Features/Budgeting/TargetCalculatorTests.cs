using System.Diagnostics.CodeAnalysis;
using Shouldly;
using Xunit;
using YHAB.SharedKernel.Budgeting;
using static YHAB.UnitTests.Features.Budgeting.BudgetTestData;

namespace YHAB.UnitTests.Features.Budgeting;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class TargetCalculatorTests
{
    [Theory]
    [InlineData(TargetCadence.Yearly, 0, 12, 50)]
    [InlineData(TargetCadence.Yearly, 0, 24, 50)]
    [InlineData(TargetCadence.Custom, 3, 3, 200)]
    [InlineData(TargetCadence.Custom, 3, 9, 200)]
    public void RepeatingTargetsStartANewContributionPeriod(TargetCadence cadence, int repeat, int offset, decimal expected)
    {
        var plan = Assigned(Create(), 600);
        var due = cadence == TargetCadence.Yearly ? new DateOnly(2026, 12, 31) : new DateOnly(2026, 3, 31);
        var category = plan.Categories[0] with { Target = new(TargetKind.SetAside, cadence, 600, January, due, repeat) };
        var month = January.AddMonths(offset);
        TargetCalculator.Needed(plan, category, month, month, 0, 600, 600).ShouldBe(expected);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(30, 70)]
    [InlineData(100, 0)]
    [InlineData(101, 0)]
    public void UndatedBalanceTargetUsesAvailableMoney(decimal available, decimal expected)
    {
        var plan = Create();
        var category = plan.Categories[0] with { Target = new(TargetKind.Balance, TargetCadence.Custom, 100, January, null) };
        TargetCalculator.Needed(plan, category, January, plan.Today, 20, available, 0).ShouldBe(expected);
    }

    [Fact]
    public void DatedBalanceTargetUsesRemainingBalanceAndRoundsUpToACent()
    {
        var plan = Create();
        var category = plan.Categories[0] with { Target = new(TargetKind.Balance, TargetCadence.Custom, 100, January, new(2026, 3, 31)) };
        TargetCalculator.Needed(plan, category, January, plan.Today, 10, 20, 10).ShouldBe(20);
        TargetCalculator.Needed(plan, category, January, plan.Today, 0, 0, 0).ShouldBe(33.34m);
    }

    [Fact]
    public void NonRepeatingTargetStopsAfterItsDueMonth()
    {
        var plan = Create();
        var category = plan.Categories[0] with { Target = new(TargetKind.SetAside, TargetCadence.Custom, 600, January, new(2026, 3, 31)) };
        TargetCalculator.Needed(plan, category, January.AddMonths(2), plan.Today, 0, 0, 0).ShouldBe(600);
        TargetCalculator.Needed(plan, category, January.AddMonths(3), plan.Today, 0, 0, 0).ShouldBe(0);
    }

    [Fact]
    public void TargetsDoNotFundBeforeStartingOrBelowZero()
    {
        var plan = Create();
        var category = plan.Categories[0] with { Target = new(TargetKind.SetAside, TargetCadence.Monthly, 100, January.AddMonths(1), null) };
        TargetCalculator.Needed(plan, category, January, plan.Today, 0, 0, 0).ShouldBe(0);
        TargetCalculator.Needed(plan, category, January.AddMonths(1), plan.Today, 101, 101, 0).ShouldBe(0);
        TargetCalculator.Needed(plan, category with { Target = null }, January, plan.Today, 0, 0, 0).ShouldBe(0);
    }

    [Theory]
    [InlineData(TargetKind.Refill, 30)]
    [InlineData(TargetKind.SetAside, 80)]
    [InlineData(TargetKind.Balance, 40)]
    public void MonthlyBehaviorDistinguishesCarryContributionsAndBalance(TargetKind kind, decimal expected)
    {
        var plan = Create();
        var category = plan.Categories[0] with { Target = new(kind, TargetCadence.Monthly, 100, January, null) };
        TargetCalculator.Needed(plan, category, January, plan.Today, 20, 60, 50).ShouldBe(expected);
    }

    [Fact]
    public void FutureRefillWaitsUntilTheMonthArrivesToUseCarryover()
    {
        var plan = Create();
        var category = plan.Categories[0] with { Target = new(TargetKind.Refill, TargetCadence.Monthly, 100, January, null) };
        TargetCalculator.Needed(plan, category, January.AddMonths(1), plan.Today, 20, 70, 50).ShouldBe(80);
        TargetCalculator.Needed(plan, category, January.AddMonths(1), new(2026, 2, 1), 20, 70, 50).ShouldBe(30);
    }

    [Fact]
    public void WeeklyTargetCountsFiveFridaysInJanuary2026()
    {
        var plan = Create();
        var category = plan.Categories[0] with { Target = new(TargetKind.SetAside, TargetCadence.Weekly, 25, January, null, Weekday: DayOfWeek.Friday) };
        TargetCalculator.Needed(plan, category, January, plan.Today, 20, 20, 0).ShouldBe(105);
        plan = Assigned(plan with { Categories = [category, plan.Categories[1]] }, 20);
        var row = BudgetCalculator.Calculate(plan, January, plan.Today).Categories[0];
        row.TargetTotal.ShouldBe(125);
        row.TargetNeeded.ShouldBe(105);
    }

    [Fact]
    public void DatedSavingsTargetDistributesRemainingNeedAcrossRemainingMonths()
    {
        var plan = Assigned(Create(), 100);
        var category = plan.Categories[0] with { Target = new(TargetKind.SetAside, TargetCadence.Custom, 600, January, new(2026, 3, 31)) };
        TargetCalculator.Needed(plan, category, January.AddMonths(1), new(2026, 2, 1), 50, 150, 100).ShouldBe(200);
        plan = plan with { Categories = [category, plan.Categories[1]], Allocations = [.. plan.Allocations, new(category.Id, January.AddMonths(1), 50)] };
        var row = BudgetCalculator.Calculate(plan, January.AddMonths(1), new(2026, 2, 1)).Categories[0];
        row.TargetTotal.ShouldBe(250);
        row.TargetNeeded.ShouldBe(200);
    }

    [Fact]
    public void DatedRefillCountsMoneyAlreadySpentDuringTheTargetPeriod()
    {
        var plan = Assigned(Create(), 100);
        var category = plan.Categories[0] with { Target = new(TargetKind.Refill, TargetCadence.Custom, 600, January, new(2026, 3, 31)) };
        plan = plan with { Categories = [category, plan.Categories[1]], Transactions = [Entry(plan, 0, -50)] };
        TargetCalculator.Needed(plan, category, January.AddMonths(1), new(2026, 2, 1), 50, 100, 50).ShouldBe(200);
    }
}
