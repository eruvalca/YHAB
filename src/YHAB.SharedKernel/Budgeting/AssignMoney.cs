namespace YHAB.SharedKernel.Budgeting;

/// <summary>Assign Money within one privately owned plan, conditional on its current revision.</summary>
public sealed record AssignMoney(long Version, Guid CategoryId, DateOnly Month, decimal Amount, bool Snoozed = false) : PlanCommand(Version);
