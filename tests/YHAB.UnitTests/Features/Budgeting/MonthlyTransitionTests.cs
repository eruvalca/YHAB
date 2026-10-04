using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Shouldly;
using Xunit;
using YHAB.SharedKernel.Budgeting;
using YHAB.Tests.Scenarios;
using static YHAB.UnitTests.Features.Budgeting.BudgetTestData;

namespace YHAB.UnitTests.Features.Budgeting;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class MonthlyTransitionTests
{
    [Theory]
    [InlineData(TargetKind.Refill, 150, 180)]
    [InlineData(TargetKind.SetAside, 165, 195)]
    [InlineData(TargetKind.Balance, 170, 200)]
    public void FundingAggregatesReplaceHistoricalAllocationsWithoutChangingMoneyOrTargets(TargetKind kind, decimal needed, decimal total)
    {
        var plan = Create(cash: 1000);
        var category = plan.Categories[0] with { Target = new(kind, TargetCadence.Custom, 500, January.AddMonths(1), January.AddMonths(3)) };
        plan = plan with
        {
            Categories = [category, plan.Categories[1]],
            Allocations = [new(category.Id, January, 30), new(category.Id, January.AddMonths(1), 110),
                new(category.Id, January.AddMonths(2), 30), new(category.Id, January.AddMonths(4), 80)],
            Transactions = [Entry(plan, 0, -40, date: January.AddMonths(1).AddDays(2))],
        };
        var starts = new Dictionary<DateOnly, BudgetMonthState>();
        var opening = BudgetCalculator.Start(plan, January);
        for (var index = 0; index < 2; index++)
        {
            starts[opening.Month] = opening;
            opening = BudgetCalculator.Advance(plan, opening, plan.Today, starts).Next;
        }
        var current = plan with { Allocations = [plan.Allocations[2]], Transactions = [] };
        var funding = new BudgetMonthFunding(80, ImmutableDictionary<Guid, decimal>.Empty.Add(category.Id, 110));
        var actual = BudgetCalculator.Advance(current, opening, plan.Today, starts, funding);
        var expected = BudgetCalculator.Advance(plan, opening, plan.Today, starts);
        JsonSerializer.Serialize(actual).ShouldBe(JsonSerializer.Serialize(expected));
        actual.Budget.AssignedInFuture.ShouldBe(80);
        actual.Budget.ReadyToAssign.ShouldBe(750);
        actual.Budget.Categories[0].Available.ShouldBe(130);
        actual.Budget.Categories[0].TargetNeeded.ShouldBe(needed);
        actual.Budget.Categories[0].TargetTotal.ShouldBe(total);
        var withoutTargets = BudgetCalculator.Advance(current, opening, plan.Today, starts, funding, includeTargets: false);
        JsonSerializer.Serialize(withoutTargets.Next).ShouldBe(JsonSerializer.Serialize(actual.Next));
        withoutTargets.Budget.Categories.ShouldAllBe(item => item.TargetNeeded == 0 && item.TargetTotal == 0);
        current.Allocations.ShouldBe([plan.Allocations[2]]);
    }

    [Fact]
    public void ManyDatedTargetsAndBackdatedCorrectionsPreserveTenYearsOfProgress()
    {
        var plan = Create(cash: 1_000_000);
        var categories = Enumerable.Range(0, 80).Select(index => plan.Categories[0] with
        {
            Id = Guid.NewGuid(),
            Name = $"Target {index}",
            Target = new(TargetKind.Refill, TargetCadence.Yearly, 1200, January.AddMonths(1), January.AddMonths(12).AddDays(-1)),
        }).ToArray();
        plan = plan with
        {
            Categories = categories,
            Today = January.AddMonths(120).AddDays(-1),
            Allocations = categories.SelectMany(category => Enumerable.Range(0, 120)
                .Select(index => new AllocationData(category.Id, January.AddMonths(index), 50))).ToArray(),
        };
        var state = BudgetCalculator.Start(plan, plan.Today);
        var starts = new Dictionary<DateOnly, BudgetMonthState>();
        for (var index = 0; index < 120; index++)
        {
            starts.Add(state.Month, state);
            var input = JsonSerializer.Serialize(state);
            var transition = BudgetCalculator.Advance(plan, state, plan.Today, starts);
            transition.Budget.Available.ShouldBe(80 * 50 * (index + 1));
            transition.Budget.ReadyToAssign.ShouldBe(1_000_000 - 80 * 50 * 120);
            if (index == 1) { transition.Budget.Categories.ShouldAllBe(row => row.TargetNeeded == 54.55m); }
            if (index == 119) { transition.Budget.Categories.ShouldAllBe(row => row.TargetNeeded == 0); }
            JsonSerializer.Serialize(state).ShouldBe(input);
            state = transition.Next;
        }
        // Correct the first assignment. Replaying from that opening must change both
        // carry and all dated target period starts, without changing unrelated targets.
        var corrected = plan with { Allocations = plan.Allocations.Select(item => item.CategoryId == categories[0].Id && item.Month == January ? item with { Amount = 75 } : item).ToArray() };
        var february = BudgetCalculator.Calculate(corrected, January.AddMonths(1), plan.Today);
        february.Categories[0].TargetNeeded.ShouldBe(52.28m);
        february.Categories.Skip(1).ShouldAllBe(row => row.TargetNeeded == 54.55m);
        var last = BudgetCalculator.Calculate(corrected, BudgetFacts.Month(plan.Today), plan.Today);
        last.Available.ShouldBe(480_025);
        last.ReadyToAssign.ShouldBe(519_975);
    }

    [Fact]
    public void TransitionCanResumeWithoutPriorTransactionsAndRetainsCreditAndCashSemantics()
    {
        var plan = Assigned(Create(cash: 1000), 100);
        plan = plan with { Transactions = [Entry(plan, 1, -120), Entry(plan, 0, -20)] };
        var first = BudgetCalculator.Advance(plan, BudgetCalculator.Start(plan, January), plan.Today, new Dictionary<DateOnly, BudgetMonthState>());
        first.Budget.CreditOverspending.ShouldBe(40);
        first.Budget.Categories[1].Available.ShouldBe(80);
        var next = BudgetCalculator.Advance(plan with { Transactions = [.. plan.Transactions, Entry(plan, 0, -999) with { Repeat = RepeatFrequency.Monthly }] }, first.Next, plan.Today, new Dictionary<DateOnly, BudgetMonthState>());
        next.Budget.ReadyToAssign.ShouldBe(900);
        next.Budget.Available.ShouldBe(80);
        next.Next.Balances[plan.Accounts[0].Id].ShouldBe(980);
        next.Next.Balances[plan.Accounts[1].Id].ShouldBe(-120);
    }
}
