namespace YHAB.SharedKernel.Budgeting;

/// <summary>Auto Assign within one plan the caller can edit, conditional on its current revision.</summary>
public sealed record AutoAssign(long Version, DateOnly Month) : PlanCommand(Version);
