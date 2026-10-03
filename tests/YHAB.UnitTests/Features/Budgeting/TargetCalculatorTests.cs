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
