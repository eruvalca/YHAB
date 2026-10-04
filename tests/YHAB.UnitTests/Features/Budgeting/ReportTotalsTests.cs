using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Shouldly;
using Xunit;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.UnitTests.Features.Budgeting;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class ReportTotalsTests
{
    [Fact]
    public void MonthlyTotalsPreserveOpeningDatesDebtRefundsIncomeAndInputValues()
    {
        var january = BudgetTestData.January;
        var plan = BudgetTestData.Create() with { Version = 17 };
        var cash = plan.Accounts[0] with { OpeningBalance = 100 };
        var card = plan.Accounts[1] with { OpeningBalance = -50 };
        var later = cash with { Id = Guid.NewGuid(), OpeningBalance = 200, OpenedOn = january.AddMonths(1).AddDays(14) };
        var future = cash with { Id = Guid.NewGuid(), OpeningBalance = 999, OpenedOn = january.AddMonths(2).AddDays(10) };
        var closed = cash with { Id = Guid.NewGuid(), Kind = AccountKind.Asset, OpeningBalance = 50, Closed = true };
        var zero = cash with { Id = Guid.NewGuid(), OpeningBalance = 0 };
        var categories = Enumerable.Range(1, 4).Select(index => plan.Categories[0] with { Id = new(index, 0, 0, new byte[8]), Name = $"Category {index}" }).ToArray();
        plan = plan with { Accounts = [cash, card, later, future, closed, zero], Categories = categories };
        var totals = new ReportTotals(new Dictionary<Guid, decimal> { [cash.Id] = -20, [card.Id] = -10 },
            [new(cash.Id, january, -100), new(card.Id, january, 100), new(cash.Id, january.AddMonths(1), 30),
                new(later.Id, january.AddMonths(1), -20), new(card.Id, january.AddMonths(2), -50)],
            [new(null, january, 100), new(categories[0].Id, january, -30), new(categories[0].Id, january, 10),
                new(categories[3].Id, january, -20), new(categories[1].Id, january, -10),
                new(null, january.AddMonths(1), -5), new(categories[0].Id, january.AddMonths(1), 20),
                new(categories[1].Id, january.AddMonths(1), -10), new(categories[2].Id, january.AddMonths(2), 7)]);
        var originalPlan = JsonSerializer.Serialize(plan);
        var originalTotals = JsonSerializer.Serialize(totals);

        var report = ReportCalculator.FromTotals(plan, january.AddDays(14), january.AddMonths(2).AddDays(9), totals);

        report.Version.ShouldBe(17);
        report.Months.ShouldBe([new(january, 100, 50, 90, 20), new(january.AddMonths(1), -5, -10, 280, 0), new(january.AddMonths(2), 0, -7, 240, 10)]);
        report.Spending.ShouldBe([new(categories[1].Id, categories[1].Name, 20), new(categories[3].Id, categories[3].Name, 20), new(categories[2].Id, categories[2].Name, -7)]);
        JsonSerializer.Serialize(plan).ShouldBe(originalPlan);
        JsonSerializer.Serialize(totals).ShouldBe(originalTotals);
    }

    [Theory]
    [InlineData(13, 0)]
    [InlineData(14, 200)]
    [InlineData(15, 200)]
    public void OpeningBalanceStartsOnItsExactDateWithinTheLastPartialMonth(int endDay, decimal assets)
    {
        var month = BudgetTestData.January.AddMonths(1);
        var plan = BudgetTestData.Create();
        plan = plan with { Accounts = [plan.Accounts[0] with { OpeningBalance = 200, OpenedOn = month.AddDays(14) }] };
        var report = ReportCalculator.FromTotals(plan, month, month.AddDays(endDay), new(new Dictionary<Guid, decimal>(), [], []));
        report.Months.ShouldBe([new(month, 0, 0, assets, 0)]);
        report.Spending.ShouldBeEmpty();
    }

    [Fact]
    public void EmptyCatalogStillProducesEveryRequestedMonth()
    {
        var from = new DateOnly(2025, 12, 31);
        var plan = BudgetTestData.Create() with { Accounts = [], Categories = [] };
        var report = ReportCalculator.FromTotals(plan, from, from.AddDays(1), new(new Dictionary<Guid, decimal>(), [], []));
        report.Months.ShouldBe([new(new(2025, 12, 1), 0, 0, 0, 0), new(new(2026, 1, 1), 0, 0, 0, 0)]);
        report.Spending.ShouldBeEmpty();
    }

    [Fact]
    public void MissingRequiredAggregateInputsFailAtTheBoundary()
    {
        var plan = BudgetTestData.Create();
        var totals = new ReportTotals(new Dictionary<Guid, decimal>(), [], []);
        Should.Throw<ArgumentNullException>(() => ReportCalculator.FromTotals(null!, plan.Today, plan.Today, totals)).ParamName.ShouldBe("catalog");
        Should.Throw<ArgumentNullException>(() => ReportCalculator.FromTotals(plan, plan.Today, plan.Today, null!)).ParamName.ShouldBe("totals");
    }
}
