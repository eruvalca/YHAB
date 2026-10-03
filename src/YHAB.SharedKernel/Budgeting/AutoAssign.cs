namespace YHAB.SharedKernel.Budgeting;

/// <summary>Auto Assign within one privately owned plan, conditional on its current revision.</summary>
public sealed record AutoAssign(long Version, DateOnly Month) : PlanCommand(Version);
