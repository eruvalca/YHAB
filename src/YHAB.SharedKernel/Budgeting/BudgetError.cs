namespace YHAB.SharedKernel.Budgeting;

/// <summary>Budget Error exchanged between the budgeting service and its clients.</summary>
public sealed record BudgetError(int Status, string Title, string Detail);
