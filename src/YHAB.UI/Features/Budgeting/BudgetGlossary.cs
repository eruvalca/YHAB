using YHAB.SharedKernel.Budgeting;

namespace YHAB.UI.Features.Budgeting;

/// <summary>Owns the user-facing copy for budget terms so the board, filters, editors, and help agree.</summary>
public static class BudgetGlossary
{
    /// <summary>Every explained term in display order.</summary>
    public static IReadOnlyList<BudgetTerm> All { get; } = Enum.GetValues<BudgetTerm>();

    public static string Label(BudgetTerm term) => term switch
    {
        BudgetTerm.ReadyToAssign => "Ready to assign",
        BudgetTerm.Overassigned => "Overassigned",
        BudgetTerm.Assigned => "Assigned",
        BudgetTerm.Activity => "Activity",
        BudgetTerm.Available => "Available",
        BudgetTerm.StillNeeded => "Still needed",
        BudgetTerm.FundTargets => "Fund targets",
        BudgetTerm.MoveMoney => "Move money",
        BudgetTerm.Refill => "Refill up to",
        BudgetTerm.SetAside => "Set aside another",
        BudgetTerm.HaveABalance => "Have a balance of",
        BudgetTerm.Snooze => "Snooze",
        _ => term.ToString(),
    };

    public static string Definition(BudgetTerm term) => term switch
    {
        BudgetTerm.ReadyToAssign => "Money you have that is not assigned to a category yet.",
        BudgetTerm.Overassigned => "You have assigned more money than you have, so Ready to assign is below zero.",
        BudgetTerm.Assigned => "Money you have given categories this month.",
        BudgetTerm.Activity => "Money spent from or added to your categories this month.",
        BudgetTerm.Available => "What a category has left after assigning and spending; a positive balance carries into next month.",
        BudgetTerm.StillNeeded => "Money this month's targets and overspent categories still need.",
        BudgetTerm.FundTargets => "Fill this month's targets from Ready to assign in one step.",
        BudgetTerm.MoveMoney => "Move money between Ready to assign and a category without changing this month's assignment.",
        BudgetTerm.Refill => "Refill up to this amount. Money left from the previous period reduces what you need to add.",
        BudgetTerm.SetAside => "Set aside new money each period, even when money is left over.",
        BudgetTerm.HaveABalance => "Build toward a balance. Spending from this category increases what you need to replace.",
        BudgetTerm.Snooze => "Pause a category's target for this month without deleting it.",
        _ => string.Empty,
    };

    public static BudgetTerm For(TargetKind kind) => kind switch
    {
        TargetKind.Refill => BudgetTerm.Refill,
        TargetKind.SetAside => BudgetTerm.SetAside,
        _ => BudgetTerm.HaveABalance,
    };
}
