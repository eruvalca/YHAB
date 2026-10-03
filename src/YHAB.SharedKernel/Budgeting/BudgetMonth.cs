namespace YHAB.SharedKernel.Budgeting;

/// <summary>Budget Month exchanged between the budgeting service and its clients.</summary>
public sealed record BudgetMonth(DateOnly Month, decimal ReadyToAssign, decimal Assigned, decimal Activity, decimal Available, decimal CashOverspending, decimal CreditOverspending, decimal AssignedInFuture, IReadOnlyList<CategoryMonth> Categories);
