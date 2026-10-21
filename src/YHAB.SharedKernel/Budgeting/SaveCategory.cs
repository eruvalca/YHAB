namespace YHAB.SharedKernel.Budgeting;

/// <summary>Save Category within one plan the caller can edit, conditional on its current revision.</summary>
public sealed record SaveCategory(long Version, CategoryData Category) : PlanCommand(Version);
