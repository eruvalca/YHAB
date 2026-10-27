using System.Diagnostics.CodeAnalysis;
using Shouldly;
using Xunit;
using YHAB.SharedKernel.Budgeting;
using YHAB.UI.Features.Budgeting;

namespace YHAB.ComponentTests.Features.Budgeting;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class WorkspaceCacheTests
{
    [Fact]
    public void RegisterOnlyRefreshKeepsTheCurrentMonthForTheSameRevision()
    {
        var cache = new WorkspaceCache();
        var today = new DateOnly(2026, 10, 3);
        var plan = new PlanSnapshot(Guid.NewGuid(), "Plan", "", today, 7, [], [], [], [], [], false, false, []) { Today = today };
        var view = new PlanView(plan, [], false);
        var month = BudgetCalculator.Calculate(plan, BudgetFacts.Month(today), today);

        cache.Store(plan.Id, view, month);
        cache.Store(plan.Id, view, null);

        cache.TryGet(plan.Id, out _, out var preserved).ShouldBeTrue();
        preserved.ShouldNotBeNull().Month.ShouldBe(BudgetFacts.Month(today));
    }

    [Fact]
    public void MissingMonthIsDroppedWhenTheRevisionOrBudgetDateChanges()
    {
        var cache = new WorkspaceCache();
        var today = new DateOnly(2026, 10, 3);
        var plan = new PlanSnapshot(Guid.NewGuid(), "Plan", "", today, 7, [], [], [], [], [], false, false, []) { Today = today };
        var month = BudgetCalculator.Calculate(plan, BudgetFacts.Month(today), today);
        cache.Store(plan.Id, new PlanView(plan, [], false), month);

        cache.Store(plan.Id, new PlanView(plan with { Version = 8 }, [], false), null);

        cache.TryGet(plan.Id, out _, out var fromNewRevision).ShouldBeTrue();
        fromNewRevision.ShouldBeNull();

        cache.Store(plan.Id, new PlanView(plan with { Version = 8 }, [], false), month);
        cache.Store(plan.Id, new PlanView(plan with { Version = 8, Today = today.AddDays(1) }, [], false), null);

        cache.TryGet(plan.Id, out _, out var fromNewDate).ShouldBeTrue();
        fromNewDate.ShouldBeNull();
    }
}
