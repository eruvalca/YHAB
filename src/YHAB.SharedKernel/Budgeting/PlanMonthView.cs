namespace YHAB.SharedKernel.Budgeting;

/// <summary>Navigation balances and a budget month read from one committed plan revision.</summary>
public sealed record PlanMonthView(PlanView View, BudgetMonth Month);
