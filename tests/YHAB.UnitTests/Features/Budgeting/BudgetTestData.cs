using YHAB.SharedKernel.Budgeting;

namespace YHAB.UnitTests.Features.Budgeting;

internal static class BudgetTestData
{
    public static DateOnly January => new(2026, 1, 1);
    public static PlanSnapshot Create(decimal cash = 1000, decimal card = 0)
    {
        var checking = new AccountData(Guid.NewGuid(), "Checking", AccountKind.Checking, cash, January, false, "");
        var credit = new AccountData(Guid.NewGuid(), "Card", AccountKind.CreditCard, card, January, false, "");
        var group = new GroupData(Guid.NewGuid(), "Everyday", 0);
        return new(Guid.NewGuid(), "Test plan", "", January, 0, [checking, credit], [group],
            [new(Guid.NewGuid(), group.Id, "Groceries", "", 0, false, null, null),
             new(Guid.NewGuid(), group.Id, "Card", "", 1, false, credit.Id, null)],
            [], [], false, false, [])
        { Today = January.AddDays(30) };
    }
    public static TransactionData Entry(PlanSnapshot plan, int account, decimal amount, int category = 0, DateOnly? date = null)
        => new(Guid.NewGuid(), plan.Accounts[account].Id, date ?? January.AddDays(1), "Market", "", amount, null,
            ClearingState.Uncleared, ClearingState.Uncleared, false, "", [new(Guid.NewGuid(), category < 0 ? null : plan.Categories[category].Id, amount, "")]);
    public static PlanSnapshot Assigned(PlanSnapshot plan, decimal amount)
        => plan with { Allocations = [new(plan.Categories[0].Id, January, amount)] };
}

