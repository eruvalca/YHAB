namespace YHAB.SharedKernel.Budgeting;

/// <summary>Update Plan within one privately owned plan, conditional on its current revision.</summary>
public sealed record UpdatePlan(long Version, string Name, string Notes) : PlanCommand(Version);
