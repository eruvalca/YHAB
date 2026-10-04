namespace YHAB.SharedKernel.Budgeting;

public sealed record BudgetMonthTransition(BudgetMonth Budget, BudgetMonthState Next);
