namespace YHAB.SharedKernel.Budgeting;

/// <summary>Save Group within one plan the caller can edit, conditional on its current revision.</summary>
public sealed record SaveGroup(long Version, GroupData Group) : PlanCommand(Version);
