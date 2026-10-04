using System.Diagnostics.CodeAnalysis;
using Shouldly;
using Xunit;
using YHAB.SharedKernel.Budgeting;
using static YHAB.UnitTests.Features.Budgeting.BudgetTestData;

namespace YHAB.UnitTests.Features.Budgeting;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class TargetPeriodTests
{
    [Theory]
    [InlineData(TargetCadence.Custom, 0, 0, 0, 2)]
    [InlineData(TargetCadence.Custom, 0, 2, 0, 2)]
    [InlineData(TargetCadence.Custom, 3, 3, 3, 5)]
    [InlineData(TargetCadence.Custom, 3, 5, 3, 5)]
    [InlineData(TargetCadence.Custom, 3, 9, 9, 11)]
    [InlineData(TargetCadence.Yearly, 3, 3, 3, 14)]
    [InlineData(TargetCadence.Yearly, 0, 15, 15, 26)]
    public void ActivePeriodIncludesDueMonthAndRepeatsFromFollowingMonth(TargetCadence cadence, int repeat, int offset, int start, int due)
    {
        var target = new TargetData(TargetKind.Refill, cadence, 600, January, January.AddMonths(3).AddDays(-1), repeat);
        TargetCalculator.PeriodFor(target, January.AddMonths(offset)).ShouldBe(new TargetPeriod(January.AddMonths(start), January.AddMonths(due)));
    }

    [Fact]
    public void MissingInactiveUndatedAndFinishedTargetsHaveNoHistoricalPeriod()
    {
        var target = new TargetData(TargetKind.SetAside, TargetCadence.Custom, 600, January, January.AddMonths(2));
        TargetCalculator.PeriodFor(null, January).ShouldBeNull();
        TargetCalculator.PeriodFor(target with { DueDate = null }, January).ShouldBeNull();
        TargetCalculator.PeriodFor(target, January.AddMonths(-1)).ShouldBeNull();
        TargetCalculator.PeriodFor(target, January.AddMonths(3)).ShouldBeNull();
        TargetCalculator.PeriodFor(target with { Cadence = TargetCadence.Monthly }, January).ShouldBeNull();
        TargetCalculator.PeriodFor(target with { Cadence = TargetCadence.Weekly }, January).ShouldBeNull();
    }
}
