using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Shouldly;
using Xunit;
using YHAB.Features.Budgeting.Services;
using YHAB.SharedKernel.Budgeting;
using static YHAB.UnitTests.Features.Budgeting.BudgetTestData;

namespace YHAB.UnitTests.Features.Budgeting;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class CalculationInvariantTests
{
    [Fact]
    public void DatedRefillUsesTheCarryAtItsOwnPeriodStart()
    {
        var plan = Create();
        var category = plan.Categories[0] with { Target = new(TargetKind.Refill, TargetCadence.Custom, 300, January.AddMonths(1), January.AddMonths(4)) };
        plan = plan with
        {
            Categories = [category, plan.Categories[1]],
            Allocations = [new(category.Id, January, 100), new(category.Id, January.AddMonths(1), 50)],
            Transactions = [Entry(plan, 0, -40, date: January.AddMonths(1))],
        };
        var month = BudgetCalculator.Calculate(plan, January.AddMonths(2), plan.Today);
        var row = month.Categories.Single(item => item.Category.Id == category.Id);
        row.TargetNeeded.ShouldBe(50);
        row.TargetTotal.ShouldBe(50);
        row.Available.ShouldBe(110);
    }
    [Theory]
    [InlineData(60)]
    [InlineData(120)]
    public void TenYearsOfCashFlowsAndAssignmentsPreserveEveryCent(int months)
    {
        var plan = Create(cash: 1000);
        var category = plan.Categories[0].Id;
        plan = plan with
        {
            Today = January.AddMonths(months).AddDays(-1),
            Transactions = Enumerable.Range(0, months).SelectMany(index => new[]
            {
                Entry(plan, 0, 100.01m, category: -1, date: January.AddMonths(index)),
                Entry(plan, 0, -30.03m, date: January.AddMonths(index).AddDays(1)),
            }).ToArray(),
            Allocations = Enumerable.Range(0, months).Select(index => new AllocationData(category, January.AddMonths(index), 40.04m)).ToArray(),
        };
        var original = JsonSerializer.Serialize(plan);
        var result = BudgetCalculator.Calculate(plan, BudgetFacts.Month(plan.Today), plan.Today);
        result.ReadyToAssign.ShouldBe(1000 + 59.97m * months);
        result.Available.ShouldBe(10.01m * months);
        BudgetFacts.Balance(plan, plan.Accounts[0], plan.Today).Working.ShouldBe(1000 + 69.98m * months);
        ReportCalculator.Months(plan, January, plan.Today).Sum(item => item.NetIncome).ShouldBe(69.98m * months);
        JsonSerializer.Serialize(BudgetCalculator.Calculate(plan, BudgetFacts.Month(plan.Today), plan.Today)).ShouldBe(JsonSerializer.Serialize(result));
        JsonSerializer.Serialize(plan).ShouldBe(original);
    }

    [Fact]
    public void SeededAssignmentAndTransferSequencesConserveMoney()
    {
        var plan = Create(cash: 10000);
        var savings = plan.Accounts[0] with { Id = Guid.NewGuid(), Name = "Savings", OpeningBalance = 0 };
        plan = plan with { Accounts = [.. plan.Accounts, savings] };
        var random = new Bogus.Randomizer(42);
        for (var index = 0; index < 100; index++)
        {
            var assigned = random.Int(0, 100000) / 100m;
            var changed = MoneyChanges.Assign(plan, new(0, plan.Categories[0].Id, January, assigned));
            changed.IsT0.ShouldBeTrue();
            plan = changed.AsT0;
            var amount = random.Int(1, 1000) / 100m;
            plan = plan with { Transactions = [.. plan.Transactions, Entry(plan, 0, -amount) with { TransferAccountId = savings.Id, Splits = [] }] };
            var month = BudgetCalculator.Calculate(plan, January, plan.Today);
            month.ReadyToAssign.ShouldBe(10000 - assigned);
            month.Available.ShouldBe(assigned);
            plan.Accounts.Sum(account => BudgetFacts.Balance(plan, account, plan.Today).Working).ShouldBe(10000);
        }
    }
}
