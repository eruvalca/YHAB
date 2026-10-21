namespace YHAB.SharedKernel.Budgeting;

/// <summary>Update Plan within one plan the caller can edit, conditional on its current revision.</summary>
public sealed record UpdatePlan(long Version, string Name, string Notes) : PlanCommand(Version);
