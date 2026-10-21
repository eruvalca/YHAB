namespace YHAB.SharedKernel.Budgeting;

/// <summary>Assign Money within one plan the caller can edit, conditional on its current revision.</summary>
public sealed record AssignMoney(long Version, Guid CategoryId, DateOnly Month, decimal Amount, bool Snoozed = false) : PlanCommand(Version);
