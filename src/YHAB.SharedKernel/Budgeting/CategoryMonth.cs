namespace YHAB.SharedKernel.Budgeting;

/// <summary>Category Month exchanged between the budgeting service and its clients.</summary>
public sealed record CategoryMonth(CategoryData Category, decimal Assigned, decimal Activity, decimal Available, decimal CashOverspending, decimal CreditOverspending, decimal TargetNeeded, decimal TargetTotal, bool Snoozed);
