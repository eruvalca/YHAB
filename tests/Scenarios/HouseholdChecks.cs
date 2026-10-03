using Shouldly;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.Tests.Scenarios;

internal static class HouseholdChecks
{
    public static void Verify(PlanSnapshot plan, int months, int monthlyCount)
    {
        plan.Transactions.Count.ShouldBe(months * monthlyCount);
        decimal[] balances = [5400 + 3880 * months, 8000 + 600 * months, 0, 0, 15000, -12000 + 200 * months];
        for (var index = 0; index < balances.Length; index++)
        {
            var account = plan.Accounts.Single(item => item.Id == HouseholdScenario.Id(index + 1));
            var balance = BudgetFacts.Balance(plan, account, HouseholdScenario.End(months));
            balance.Working.ShouldBe(balances[index], account.Name);
            balance.Cleared.ShouldBe(balances[index], account.Name);
            balance.Uncleared.ShouldBe(0);
        }

        var budget = BudgetCalculator.Calculate(plan, HouseholdScenario.Start.AddMonths(months - 1), HouseholdScenario.End(months));
        budget.ReadyToAssign.ShouldBe(13400 + 4050 * months);
        budget.Assigned.ShouldBe(months == 1 ? 4950 : 4350);
        budget.CashOverspending.ShouldBe(0);
        budget.CreditOverspending.ShouldBe(0);
        budget.Available.ShouldBe(430 * months);
        decimal[] carry = [0, 30, 40, 50, 50, 150, 110, 0, 0, 0];
        for (var index = 0; index < carry.Length; index++)
        {
            var row = budget.Categories.Single(item => item.Category.Id == HouseholdScenario.Id(100 + index));
            row.Available.ShouldBe(carry[index] * months, row.Category.Name);
        }

        budget.Categories.Single(item => item.Category.Id == HouseholdScenario.Id(105)).TargetNeeded.ShouldBe(0);
        var reports = ReportCalculator.Months(plan, HouseholdScenario.Start, HouseholdScenario.End(months));
        reports.Count.ShouldBe(months);
        for (var index = 0; index < months; index++)
        {
            reports[index].Income.ShouldBe(8400);
            reports[index].Expense.ShouldBe(3920);
            reports[index].NetIncome.ShouldBe(4480);
            reports[index].NetWorth.ShouldBe(16400 + 4680 * (index + 1));
        }

        var spending = ReportCalculator.Spending(plan, HouseholdScenario.Start, HouseholdScenario.End(months));
        spending.Sum(item => item.Amount).ShouldBe(3920 * months);
        spending.Single(item => item.CategoryId == HouseholdScenario.Id(102)).Amount.ShouldBe(810 * months);
        spending.Single(item => item.CategoryId == HouseholdScenario.Id(103)).Amount.ShouldBe(350 * months);
    }
}
