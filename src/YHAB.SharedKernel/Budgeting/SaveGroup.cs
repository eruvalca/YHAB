namespace YHAB.SharedKernel.Budgeting;

/// <summary>Save Group within one privately owned plan, conditional on its current revision.</summary>
public sealed record SaveGroup(long Version, GroupData Group) : PlanCommand(Version);
